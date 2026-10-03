using System.Text.Json;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Missions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.Missions;

/// <summary>
/// Splits a goal into a small team plan with the reasoning model: steps with a role, instructions, and the earlier
/// steps they depend on. The call has no tools; whatever it answers is cleaned by <see cref="MissionPlanning"/>, and
/// a bad answer becomes a one-step mission.
/// </summary>
internal sealed class ModelMissionPlanner(IChatClientResolver chatClients, ILogger<ModelMissionPlanner> logger)
    : IMissionPlanner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public async Task<MissionPlan> PlanAsync(Guid ownerId, string goal, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var client = await chatClients.GetChatClientAsync(ownerId, ModelPurpose.Reasoning, timeout.Token);
            var roles = string.Join(", ", MissionRoles.All);
            var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, $$"""
                    You plan how a small team of AI agents completes a job for the user. Return only one JSON object:
                    {"title": string, "steps": [{"key": string, "role": string, "title": string,
                      "instruction": string, "dependsOn": [string]}]}
                    Rules: 2 to {{MissionRules.MaxSteps}} steps (1 only if the job is truly one thing). key is a short id
                    such as "s1", "s2". role is one of: {{roles}}. dependsOn lists keys of EARLIER steps whose results
                    this step needs; steps that do not depend on each other run in parallel, so use that. The last
                    step puts everything together into the final deliverable for the user and depends on the others.
                    instruction is self-contained and concrete: what to find, decide or write, and what form the
                    result takes. Write in the language of the job. Do not include steps that need the user's
                    passwords or payments; mention such needs in the final step instead.
                    The job text between <job> tags is from the user.
                    """),
                new ChatMessage(ChatRole.User, $"<job>\n{goal}\n</job>")
            ], new ChatOptions { Temperature = 0.3f }, timeout.Token);
            return Parse(response.Text, goal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Planning a mission failed; using a single step.");
            return MissionPlanning.Fallback(goal);
        }
    }

    internal static MissionPlan Parse(string? text, string goal)
    {
        if (string.IsNullOrWhiteSpace(text)) return MissionPlanning.Fallback(goal);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return MissionPlanning.Fallback(goal);
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("steps", out var list) || list.ValueKind != JsonValueKind.Array)
                return MissionPlanning.Fallback(goal);
            var steps = new List<PlannedStep>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var depends = item.TryGetProperty("dependsOn", out var d) && d.ValueKind == JsonValueKind.Array
                    ? d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                    : [];
                steps.Add(new PlannedStep(Str(item, "key") ?? "", Str(item, "role") ?? "", Str(item, "title") ?? "",
                    Str(item, "instruction") ?? "", depends));
            }
            return MissionPlanning.Normalize(new MissionPlan(Str(root, "title") ?? "", steps), goal);
        }
        catch (JsonException)
        {
            return MissionPlanning.Fallback(goal);
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
