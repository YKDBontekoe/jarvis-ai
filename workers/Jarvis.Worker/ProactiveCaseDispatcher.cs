using Jarvis.Application.Proactivity;

namespace Jarvis.Worker;

/// <summary>Repairs the DB-to-Temporal gap after a crash, including newly resumed responsibilities.</summary>
internal sealed class ProactiveCaseDispatcher(IServiceScopeFactory scopes, ILogger<ProactiveCaseDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var due = await scope.ServiceProvider.GetRequiredService<IProactiveRepository>().DueAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (due.Count == 0) continue;
                var scheduler = scope.ServiceProvider.GetRequiredService<IProactiveCaseScheduler>();
                foreach (var value in due.Where(c => c.Enabled && !c.Terminal && c.Status is not ("paused" or "suggested")))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await scheduler.ScheduleProactiveAsync(value.OwnerId, value.Id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Proactive work dispatch deferred ({ErrorType}).", error.GetType().Name); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
