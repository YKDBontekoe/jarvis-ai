using Jarvis.Application.Workflows;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class DailyBriefingActivities(IServiceScopeFactory scopeFactory) : DailyBriefingActivityContract
{
    [Activity("ResolveDailyBriefingSchedule")]
    public override async Task<DailyBriefingSchedule> ResolveScheduleAsync(DailyBriefingWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        await using var scope = scopeFactory.CreateAsyncScope();
        var preference = await scope.ServiceProvider.GetRequiredService<IDailyBriefingRepository>()
            .GetAsync(input.OwnerId, activity.CancellationToken);
        var lastDelivered = preference is not null && preference.WorkflowId == input.WorkflowId
            ? preference.LastDeliveredDate
            : null;
        return DailyBriefingClock.ResolveNext(DateTimeOffset.UtcNow, input.LocalTime, input.TimeZoneId,
            lastDelivered);
    }

    [Activity("DeliverDailyBriefing")]
    public override async Task<bool> DeliverAsync(DailyBriefingActivityInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("daily_briefing.deliver");
        trace?.SetTag("jarvis.owner.id", input.OwnerId);
        trace?.SetTag("jarvis.briefing.date", input.LocalDate.ToString("yyyy-MM-dd"));
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDailyBriefingRepository>()
            .DeliverAsync(input, activity.CancellationToken);
    }
}
