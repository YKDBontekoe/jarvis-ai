using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Conversations;
using Jarvis.Application.Diagnostics;
using Jarvis.Application.Workflows;

namespace Jarvis.Agents;

/// <summary>
/// Lets Jarvis diagnose and propose fixes to its own code. A fix always ends as a pull request that the owner
/// reviews and merges in the app; nothing here can merge, and the actual code change runs in the same isolated,
/// network-less snapshot as every other coding task.
/// </summary>
internal sealed class SelfFixAgentTools(
    CodexCodingTools coding,
    ICodingPullRequestService pullRequests,
    INotificationRepository notifications,
    IRecentFaultLog? faults,
    ICurrentUser currentUser,
    string repositoryName)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Description("List the most recent distinct warnings and errors raised inside Jarvis's own code: log message templates, exception types, and the Jarvis code locations where they were thrown. No user data is included. Use this when something Jarvis does is failing or the user reports a bug, before proposing a fix with ProposeJarvisFix.")]
    public string GetRecentJarvisFaults(
        [Description("How many distinct faults to return, newest first (1 to 30).")] int limit = 15)
    {
        if (faults is null) return "Fault history is not available in this process.";
        var recent = faults.Recent(Math.Clamp(limit, 1, 30));
        return recent.Count == 0
            ? "No recent warnings or errors."
            : JsonSerializer.Serialize(recent, JsonOptions);
    }

    [Description("Fix a bug or make an improvement in Jarvis's own source code. Jarvis copies its repository into an isolated snapshot, has Codex make the change there, and opens a GitHub pull request the user reviews and approves in the app (Coding runs). This never merges anything. Use it when Jarvis misbehaves, a tool is broken, or the user asks Jarvis to change itself. Describe the problem and the behaviour you expect; check GetRecentJarvisFaults first for a bug. Requires approval, and one approval covers making the change and opening the pull request.")]
    public async Task<string> ProposeJarvisFixAsync(
        [Description("A short pull request title in the imperative, for example 'Skip MCP servers that fail to start'.")] string title,
        [Description("What is wrong or missing, with the concrete symptoms or error messages the user saw.")] string problem,
        [Description("What should happen instead, and how someone can tell it is fixed.")] string expectedBehavior,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(problem))
            return "A title and a description of the problem are required.";

        var prompt = BuildPrompt(title.Trim(), problem.Trim(), expectedBehavior?.Trim() ?? string.Empty,
            faults?.Recent(8) ?? []);
        var result = await coding.RunCodingTaskAsync(repositoryName, prompt, cancellationToken);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        var completed = root.TryGetProperty("completed", out var flag) && flag.ValueKind == JsonValueKind.True;
        var summary = root.TryGetProperty("summary", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
        if (!completed || !root.TryGetProperty("id", out var idElement) || !idElement.TryGetGuid(out var runId))
        {
            var reason = root.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String
                ? why.GetString()
                : root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null;
            return $"The fix attempt did not finish. {reason}".Trim();
        }

        var changed = root.TryGetProperty("changedFiles", out var files) ? files.GetString() : null;
        if (string.IsNullOrWhiteSpace(changed))
            return $"Codex finished but changed no files, so there is nothing to review. {Trim(summary, 600)}";

        try
        {
            var pr = await pullRequests.PublishAsync(runId, currentUser.OwnerId, title.Trim(), null, cancellationToken);
            await NotifyAsync("coding.pr.ready", "Jarvis proposed a fix", $"{pr.Title} — review and approve it in Coding runs.",
                runId, cancellationToken);
            return $"Opened pull request #{pr.Number} ({pr.Url}). The user reviews it and decides whether to merge in the app under Coding runs. It has not been merged. {Trim(summary, 600)}";
        }
        catch (CodingPullRequestException exception)
        {
            await NotifyAsync("coding.run.ready", "A fix is ready for review",
                $"{title.Trim()} — open Coding runs to review it.", runId, cancellationToken);
            return $"The change is ready in coding run {runId}, but no pull request was opened: {exception.Message} " +
                   $"The user can review it and open the pull request from Coding runs. {Trim(summary, 600)}";
        }
    }

    private Task NotifyAsync(string type, string title, string body, Guid runId, CancellationToken cancellationToken) =>
        notifications.CreateAsync(currentUser.OwnerId, type, title, body.Length > 400 ? body[..400] : body, runId,
            cancellationToken);

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    internal static string BuildPrompt(string title, string problem, string expected, IReadOnlyList<FaultEntry> faults)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Jarvis is improving its own source code. Make the smallest correct change for the issue below.");
        builder.AppendLine("Follow the repository's AGENTS.md and existing conventions. Respect owner scoping and never bypass approvals.");
        builder.AppendLine("Add or update unit tests next to the code you change. The network is disabled, so a build may not restore packages; say plainly which checks you could and could not run.");
        builder.AppendLine("Do not modify .github, CI, Docker or compose files, EF migrations (unless the fix truly needs one), or credentials.");
        builder.AppendLine("Leave everything uncommitted; a person reviews the pull request.");
        builder.AppendLine();
        builder.AppendLine($"Change: {title}");
        builder.AppendLine();
        builder.AppendLine("Problem:");
        builder.AppendLine(problem);
        if (!string.IsNullOrWhiteSpace(expected))
        {
            builder.AppendLine();
            builder.AppendLine("Expected behaviour:");
            builder.AppendLine(expected);
        }

        if (faults.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent faults in the running server (templates and code locations only):");
            foreach (var fault in faults)
            {
                builder.Append("- [").Append(fault.Level).Append("] ").Append(fault.Template);
                if (fault.ExceptionType is not null) builder.Append(" — ").Append(fault.ExceptionType);
                if (fault.Frames.Count > 0) builder.Append(" at ").Append(fault.Frames[0]);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
