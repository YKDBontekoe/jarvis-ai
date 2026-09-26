using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents;

#pragma warning disable MAAI001
public sealed class CurrentContextCompactionProvider(CompactionStrategy strategy,
    ILogger<CurrentContextCompactionProvider> logger) : AIContextProvider
{
    protected override async ValueTask<AIContext> InvokingCoreAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.AIContext.Messages is null) return context.AIContext;

        // Build from the complete current request. The SDK's incremental index can mistake
        // repeated reference context for its previous endpoint and discard new approval responses.
        return new AIContext
        {
            Instructions = context.AIContext.Instructions,
            Tools = context.AIContext.Tools,
            Messages = await CompactionProvider.CompactAsync(strategy, context.AIContext.Messages,
                logger, cancellationToken)
        };
    }
}
#pragma warning restore MAAI001
