using Jarvis.Application.Events;

namespace Jarvis.Worker;

/// <summary>Keeps the event log to the last 90 days; the activity feed and Jarvis's situation only read recent events.</summary>
internal sealed class OwnerEventRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OwnerEventRetentionWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<IOwnerEventRepository>()
                    .PruneAsync(timeProvider.GetUtcNow() - Retention, stoppingToken);
                if (removed > 0) logger.LogInformation("Pruned {Count} old owner events.", removed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Pruning old owner events failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
