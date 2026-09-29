using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class ConditionWatchActivities(IServiceScopeFactory scopeFactory)
    : ConditionWatchActivityContract
{
    [Activity("CheckConditionWatch")]
    public override async Task<ConditionWatchCheckResult> CheckAsync(ConditionWatchWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("condition_watch.check");
        trace?.SetTag("jarvis.watch.id", input.WatchId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var watches = scope.ServiceProvider.GetRequiredService<IConditionWatchRepository>();
        var watch = await watches.GetForExecutionAsync(input.WatchId, activity.CancellationToken);
        if (watch is null || watch.Status != "active") return new ConditionWatchCheckResult(false, 15);
        var metrics = scope.ServiceProvider.GetRequiredService<WatchMetricReader>();
        var value = await metrics.ReadAsync(watch, activity.CancellationToken);
        return await watches.RecordCheckAsync(input.WatchId, value, DateTimeOffset.UtcNow,
            activity.CancellationToken);
    }

    [Activity("FailConditionWatch")]
    public override async Task FailAsync(ConditionWatchWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("condition_watch.fail");
        trace?.SetTag("jarvis.watch.id", input.WatchId);
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IConditionWatchRepository>()
            .MarkFailedAsync(input.WatchId, activity.CancellationToken);
    }
}
