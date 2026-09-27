using Jarvis.Agents.Memory;
using Jarvis.Application.Memory;

namespace Jarvis.Worker;

/// <summary>Continuously backfills memory embeddings and temporal knowledge-graph facts.</summary>
internal sealed class MemoryIndexingWorker(IServiceScopeFactory scopeFactory, ILogger<MemoryIndexingWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await IndexOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Memory indexing pass failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task IndexOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> owners;
        await using (var scope = scopeFactory.CreateAsyncScope())
            owners = await scope.ServiceProvider.GetRequiredService<IMemoryIndexRepository>()
                .ListOwnersNeedingIndexAsync(50, cancellationToken);
        foreach (var ownerId in owners)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<WorkerCurrentUser>().SetOwner(ownerId);
            try
            {
                var (embedded, graphIndexed) = await scope.ServiceProvider.GetRequiredService<MemoryIndexer>()
                    .IndexOwnerAsync(ownerId, cancellationToken);
                if (embedded + graphIndexed > 0)
                    logger.LogInformation("Indexed memories for {OwnerId}: {Embedded} embeddings, {Graph} graph updates.",
                        ownerId, embedded, graphIndexed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Memory indexing failed for owner {OwnerId}.", ownerId);
            }
        }
    }
}
