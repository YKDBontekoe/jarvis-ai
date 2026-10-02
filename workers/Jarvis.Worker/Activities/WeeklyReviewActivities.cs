using Jarvis.Application.Reviews;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class WeeklyReviewActivities(IServiceScopeFactory scopeFactory) : WeeklyReviewActivityContract
{
    [Activity("ResolveWeeklyReviewSchedule")]
    public override async Task<WeeklyReviewSchedule> ResolveScheduleAsync(WeeklyReviewWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWeeklyReviewService>()
            .ResolveScheduleAsync(input.OwnerId, activity.CancellationToken);
    }

    [Activity("DeliverWeeklyReview")]
    public override async Task<bool> DeliverAsync(WeeklyReviewActivityInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("weekly_review.deliver");
        trace?.SetTag("jarvis.owner.id", input.OwnerId);
        trace?.SetTag("jarvis.review.week", input.WeekStart.ToString("yyyy-MM-dd"));
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWeeklyReviewService>()
            .DeliverAsync(input, activity.CancellationToken);
    }
}
