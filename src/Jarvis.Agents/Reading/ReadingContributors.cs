using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Reading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.Reading;

/// <summary>
/// Reading-list tools. Saving a link the user shared and changing their own list need no approval; saving a link
/// Jarvis found itself does, because fetching it calls an address the user did not give.
/// </summary>
internal sealed class ReadingToolContributor(
    IReadingListService reading,
    ReadingTurnLinks turnLinks,
    IAuditEventStore audit,
    ICurrentUser currentUser,
    ILoggerFactory loggerFactory) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new ReadingAgentTools(reading, turnLinks, audit, currentUser,
            loggerFactory.CreateLogger<ReadingAgentTools>());
        yield return AIFunctionFactory.Create(tools.SaveForLaterAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.SaveFoundLinkForLaterAsync));
        yield return AIFunctionFactory.Create(tools.GetReadingListAsync);
        yield return AIFunctionFactory.Create(tools.MarkReadingItemAsync);
        yield return AIFunctionFactory.Create(tools.RemoveFromReadingListAsync);
    }
}

/// <summary>Captures the links in the user's message and tells the agent how big the reading list is.</summary>
internal sealed class ReadingContextContributor(IReadingListService reading, ReadingTurnLinks turnLinks)
    : IAgentContextContributor
{
    internal const string Guidance = """
        Reading list: when the user shares or pastes a link to read later ("lees dit later", "save this for later", or a bare link with "later"), call SaveForLater right away; no confirmation is needed. Jarvis fetches the page and adds a summary and reading time on its own. For "vat mijn leeslijst samen" or "what should I read", call GetReadingList and answer from it, grouping by theme and mentioning reading times. Use MarkReadingItem when they finished something and RemoveFromReadingList when they no longer want it.
        """;

    public int Order => 47;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new ReadingProvider(reading, turnLinks, context.OwnerId)];

    private sealed class ReadingProvider(IReadingListService reading, ReadingTurnLinks turnLinks, Guid ownerId)
        : MessageAIContextProvider
    {
        protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            turnLinks.Capture(context.RequestMessages?.Where(message => message.Role == ChatRole.User)
                .LastOrDefault()?.Text);
            var all = await reading.ListAsync(ownerId, cancellationToken);
            var unread = all.Where(item => !item.IsRead).ToArray();
            var text = Guidance.TrimEnd();
            if (unread.Length > 0)
                text += $"\nThe user has {unread.Length} unread {(unread.Length == 1 ? "link" : "links")} on their " +
                        $"reading list (about {unread.Sum(item => item.ReadingMinutes ?? 0)} min).";
            return [new ChatMessage(ChatRole.User, text)];
        }
    }
}
