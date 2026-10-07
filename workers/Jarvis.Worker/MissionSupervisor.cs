using Jarvis.Application.Missions;

namespace Jarvis.Worker;

/// <summary>
/// Keeps running missions moving: every few seconds (sooner while any mission is running, so a finished step hands
/// over quickly) it collects the results of finished steps and starts the steps that are ready. Mission state lives
/// in the database, so this loop can stop and start at any time, and a step is claimed with a conditional update so
/// two workers never start the same one.
/// </summary>
internal sealed class MissionSupervisor(IServiceScopeFactory scopeFactory, ILogger<MissionSupervisor> logger)
    : BackgroundService
{
    internal static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(3);

    internal static TimeSpan NextInterval(int runningMissions) => runningMissions > 0 ? BusyInterval : IdleInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                running = await PassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Mission supervisor pass failed; it will be retried.");
            }

            try
            {
                await Task.Delay(NextInterval(running), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Advances every running mission once and returns how many there were.</summary>
    private async Task<int> PassAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<IMissionRepository>()
                .ListRunningIdsAsync(cancellationToken);

        foreach (var id in ids)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                var mission = await scope.ServiceProvider.GetRequiredService<IMissionRepository>()
                    .GetForSupervisorAsync(id, cancellationToken);
                if (mission is null) continue;
                scope.ServiceProvider.GetRequiredService<WorkerCurrentUser>().SetOwner(mission.OwnerId);
                await scope.ServiceProvider.GetRequiredService<IMissionService>().AdvanceAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Advancing mission {MissionId} failed; it will be retried.", id);
            }
        }

        return ids.Count;
    }
}
