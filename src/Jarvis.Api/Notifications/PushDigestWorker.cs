using Jarvis.Application.Modes;

namespace Jarvis.Api.Notifications;

/// <summary>
/// Sends the "while you were away" digest once a mode such as Sleep or Meeting lets pushes through again. Held pushes
/// are only noticed when a mode changes, so this checks owners with something held once a minute.
/// </summary>
public sealed class PushDigestWorker(IServiceScopeFactory scopeFactory, ILogger<PushDigestWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await FlushAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Sending the push digest failed; it will be retried.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }

    internal async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var digest = scope.ServiceProvider.GetRequiredService<IPushDigestService>();
        foreach (var ownerId in await digest.ListOwnersAsync(cancellationToken))
        {
            try
            {
                await digest.FlushIfDueAsync(ownerId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Push digest for owner {OwnerId} could not be sent.", ownerId);
            }
        }
    }
}
