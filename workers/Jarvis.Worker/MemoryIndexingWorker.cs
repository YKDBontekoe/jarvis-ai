using Jarvis.Agents.Memory;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Infrastructure.Files;

namespace Jarvis.Worker;

/// <summary>Continuously backfills memory embeddings, temporal knowledge-graph facts, and file chunk embeddings.</summary>
internal sealed class MemoryIndexingWorker(IServiceScopeFactory scopeFactory, ILogger<MemoryIndexingWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            // Memory and file indexing fail independently: one broken pass must not starve the other.
            if (!await RunPassAsync(IndexOnceAsync, "Memory", stoppingToken)) return;
            if (!await RunPassAsync(IndexFilesOnceAsync, "File chunk", stoppingToken)) return;
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> RunPassAsync(Func<CancellationToken, Task> pass, string name, CancellationToken stoppingToken)
    {
        try
        {
            await pass(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{Pass} indexing pass failed; it will be retried.", name);
        }
        return true;
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

    private async Task IndexFilesOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> owners;
        await using (var scope = scopeFactory.CreateAsyncScope())
            owners = await scope.ServiceProvider.GetRequiredService<IFileContentRepository>()
                .ListOwnersNeedingEmbeddingAsync(50, cancellationToken);
        foreach (var ownerId in owners)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<WorkerCurrentUser>().SetOwner(ownerId);
            try
            {
                var embedded = await scope.ServiceProvider.GetRequiredService<FileChunkIndexer>()
                    .IndexOwnerAsync(ownerId, cancellationToken);
                if (embedded > 0)
                    logger.LogInformation("Embedded {Count} file chunks for {OwnerId}.", embedded, ownerId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "File chunk embedding failed for owner {OwnerId}.", ownerId);
            }
        }
    }
}
