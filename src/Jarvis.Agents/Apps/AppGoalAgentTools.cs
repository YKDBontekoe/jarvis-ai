using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Apps;

internal sealed class AppGoalToolContributor(IAppGoalService goals, ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new AppGoalAgentTools(goals, currentUser);
        yield return AIFunctionFactory.Create(tools.FindAppForGoalAsync);
    }
}

/// <summary>
/// Read-only discovery: which connected or connectable app can do what the user wants, in plain words. Connecting
/// happens in the app (secrets never go through chat), and nothing is connected, changed or sent from here.
/// </summary>
internal sealed class AppGoalAgentTools(IAppGoalService goals, ICurrentUser currentUser)
{
    [Description("Find which app Jarvis can use for something the user wants help with (for example 'manage my calendar' or 'deal with my email'): whether it is already connected, what Jarvis could read, prepare and change through it, and the real next step to connect it. Say honestly when no app covers the goal; never claim a capability the result does not list. Do not ask the user to type passwords or tokens in chat: connecting is done in Apps.")]
    public async Task<string> FindAppForGoalAsync(
        [Description("What the user wants help with, in their words.")] string goal,
        CancellationToken cancellationToken = default)
    {
        var search = await goals.SearchAsync(currentUser.OwnerId, goal, cancellationToken);
        if (search.Unmatched) return search.Suggestion ?? "No app covers that.";
        var text = new StringBuilder();
        foreach (var item in search.Goals)
        {
            text.Append("- ").Append(item.Title).Append(" via ").Append(item.PackName).Append(": ")
                .AppendLine(item.Status == AppGoalStatuses.Ready ? "connected" : "not connected");
            text.Append("  Reads: ").AppendLine(Join(item.Capabilities.Reads));
            text.Append("  Prepares: ").AppendLine(Join(item.Capabilities.Prepares));
            text.Append("  Changes: ").AppendLine(Join(item.Capabilities.Changes));
            text.Append("  Next: ").AppendLine(item.NextStep);
        }
        return text.ToString();
    }

    private static string Join(IReadOnlyList<string> items) => items.Count == 0 ? "nothing" : string.Join(", ", items);
}
