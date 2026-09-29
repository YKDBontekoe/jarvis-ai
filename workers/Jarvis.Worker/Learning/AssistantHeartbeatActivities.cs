using Jarvis.Application.Learning;
using Jarvis.Application.Settings;
using Jarvis.Worker.Activities;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Learning;

internal sealed class AssistantHeartbeatActivities(IServiceScopeFactory scopeFactory) : AssistantHeartbeatActivityContract
{
    [Activity("RunAssistantHeartbeat")]
    public override async Task<HeartbeatRunResult> RunAsync(HeartbeatWorkflowInput input)
    {
        var cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("heartbeat.run");
        using var heartbeat = new ActivityHeartbeat(input.OwnerId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var settings = await services.GetRequiredService<IOwnerSettingsStore>()
            .GetAsync<LearningSettings>(input.OwnerId, SettingsSections.Learning, cancellationToken);
        if (settings is not { HeartbeatEnabled: true })
            return new HeartbeatRunResult(false, 0);
        services.GetRequiredService<WorkerCurrentUser>().SetOwner(input.OwnerId);
        await services.GetRequiredService<Jarvis.Agents.Learning.HeartbeatService>()
            .RunAsync(input.OwnerId, cancellationToken);
        return new HeartbeatRunResult(true, settings.HeartbeatMinutes);
    }
}
