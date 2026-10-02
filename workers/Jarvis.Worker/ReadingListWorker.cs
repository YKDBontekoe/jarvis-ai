using Jarvis.Application.Reading;

namespace Jarvis.Worker;

/// <summary>Fetches and summarizes links saved to the reading list, retrying a failed fetch a few times.</summary>
internal sealed class ReadingListWorker(IServiceScopeFactory scopeFactory, ILogger<ReadingListWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Reading list pass failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Reading.ReadingItem> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
            due = await scope.ServiceProvider.GetRequiredService<IReadingRepository>()
                .ListDueAsync(DateTimeOffset.UtcNow, BatchSize, cancellationToken);
        foreach (var item in due)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<WorkerCurrentUser>().SetOwner(item.OwnerId);
            try
            {
                var processed = await scope.ServiceProvider.GetRequiredService<ReadingFetchProcessor>()
                    .ProcessAsync(item, cancellationToken);
                // Log the id and outcome only: links and page text stay out of the logs.
                logger.LogInformation("Reading item {ItemId} is {Status} after {Attempts} attempt(s).", processed.Id,
                    processed.Status, processed.FetchAttempts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Reading item {ItemId} could not be processed.", item.Id);
            }
        }
    }
}
