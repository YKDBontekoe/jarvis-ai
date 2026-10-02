using Jarvis.Application.Habits;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class HabitCheckInActivities(IServiceScopeFactory scopeFactory) : HabitCheckInActivityContract
{
    [Activity("ResolveHabitCheckIn")]
    public override async Task<HabitCheckInSchedule> ResolveAsync(HabitCheckInWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IHabitService>()
            .ResolveCheckInAsync(input.OwnerId, DateTimeOffset.UtcNow, activity.CancellationToken);
    }

    [Activity("DeliverHabitCheckIn")]
    public override async Task<bool> DeliverAsync(HabitCheckInDelivery input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("habit_checkin.deliver");
        trace?.SetTag("jarvis.owner.id", input.OwnerId);
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<WorkerCurrentUser>().SetOwner(input.OwnerId);
        return await scope.ServiceProvider.GetRequiredService<IHabitService>()
            .DeliverCheckInAsync(input.OwnerId, input.LocalDate, activity.CancellationToken);
    }
}
