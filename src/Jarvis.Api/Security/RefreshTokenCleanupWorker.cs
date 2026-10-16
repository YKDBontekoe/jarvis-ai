using Jarvis.Infrastructure.Identity;

namespace Jarvis.Api.Security;

/// <summary>
/// Every refresh issues a new token row, so without this the table grows with each sign-in session for good. Tokens
/// are kept for a day after they expire, then removed.
/// </summary>
public sealed class RefreshTokenCleanupWorker(IServiceScopeFactory scopeFactory, TimeProvider clock,
    ILogger<RefreshTokenCleanupWorker> logger) : BackgroundService
{
    public static readonly TimeSpan KeepAfterExpiry = TimeSpan.FromDays(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<RefreshTokenStore>()
                    .PruneAsync(clock.GetUtcNow() - KeepAfterExpiry, stoppingToken);
                if (removed > 0) logger.LogInformation("Removed {Count} expired refresh tokens.", removed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Removing expired refresh tokens failed; it will be retried.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }
}
