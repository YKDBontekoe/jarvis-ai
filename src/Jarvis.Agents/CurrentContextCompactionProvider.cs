using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents;

#pragma warning disable MAAI001
public sealed class CurrentContextCompactionProvider(CompactionStrategy strategy,
    ILogger<CurrentContextCompactionProvider> logger, RollingSummaryCompaction? summaries = null) : AIContextProvider
{
    protected override async ValueTask<AIContext> InvokingCoreAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.AIContext.Messages is null) return context.AIContext;

        // Build from the complete current request. The SDK's incremental index can mistake
        // repeated reference context for its previous endpoint and discard new approval responses.
        // The oldest turns are summarized first; the strategy then only acts when that was not enough.
        var messages = summaries is null
            ? context.AIContext.Messages
            : summaries.Compact(context.AIContext.Messages as IList<ChatMessage> ?? context.AIContext.Messages.ToList());
        return new AIContext
        {
            Instructions = context.AIContext.Instructions,
            Tools = context.AIContext.Tools,
            Messages = await CompactionProvider.CompactAsync(strategy, messages,
                logger, cancellationToken)
        };
    }
}
#pragma warning restore MAAI001
