using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Journal;
using Jarvis.Application.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.Journal;

internal sealed class JournalToolContributor(IJournalService journal, IDailyBriefingRepository briefings,
    IAuditEventStore audit, ICurrentUser currentUser, ILoggerFactory loggerFactory,
    TimeProvider? timeProvider = null) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        if (context.IsBackgroundTask) yield break;
        var tools = new JournalAgentTools(journal, briefings, audit, currentUser,
            loggerFactory.CreateLogger<JournalAgentTools>(), timeProvider ?? TimeProvider.System, context.Profile);
        yield return AIFunctionFactory.Create(tools.SaveJournalEntryAsync);
        yield return AIFunctionFactory.Create(tools.ListJournalEntriesAsync);
    }
}

/// <summary>Tells the agent how to help the user journal, without loading entries into every turn.</summary>
internal sealed class JournalContextContributor : IAgentContextContributor
{
    internal const string Guidance = """
        Journaling: the user keeps a private journal in Jarvis. When they ask to journal, or talk through their day, listen and ask one gentle follow-up at a time (highlights, what was hard, what they are grateful for), then offer to save it with SaveJournalEntry. Ask for a 1-5 mood, energy, or stress rating or a 1-10 day rating only if they want to give one; never invent ratings. Saved entries are part of memory, so use SearchMemory to recall what they wrote before. Do not give unsolicited advice about entries.
        """;

    public int Order => 45;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        context.IsBackgroundTask ? [] : [new StaticGuidanceProvider(Guidance)];

    private sealed class StaticGuidanceProvider(string text) : MessageAIContextProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, text)]);
    }
}
