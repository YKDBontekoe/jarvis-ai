using Microsoft.Agents.AI;

namespace Jarvis.Agents;

/// <summary>
/// A context contributor whose providers only carry guidance for one tool group (see <see cref="ToolGroups"/>), so
/// the guidance is sent on the turns that group is offered and left out otherwise.
/// </summary>
internal interface IGroupedContextContributor : IAgentContextContributor
{
    string ToolGroupId { get; }
}

/// <summary>
/// Runs <paramref name="inner"/> only while <paramref name="groupId"/> is active for this turn: the same rule
/// <see cref="ToolGroupChatClient"/> uses to offer the group's tools, applied to the stored history plus the new request.
/// </summary>
internal sealed class ToolGroupGatedContextProvider(AIContextProvider inner, string groupId,
    InMemoryChatHistoryProvider history) : AIContextProvider
{
    public override IReadOnlyList<string> StateKeys => inner.StateKeys;

    protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var request = context.AIContext.Messages ?? [];
        var past = context.Session is null ? [] : history.GetMessages(context.Session);
        return ToolGroups.ActiveGroups([.. past, .. request]).Contains(groupId)
            ? inner.InvokingAsync(context, cancellationToken)
            : new ValueTask<AIContext>(context.AIContext);
    }

    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default) =>
        inner.InvokedAsync(context, cancellationToken);
}
