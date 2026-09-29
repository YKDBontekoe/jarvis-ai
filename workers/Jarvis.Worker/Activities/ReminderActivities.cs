using Jarvis.Application.Workflows;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class ReminderActivities(IServiceScopeFactory scopeFactory) : ReminderActivityContract
{
    [Activity("DeliverReminder")]
    public override async Task DeliverReminderAsync(ReminderWorkflowInput reminder)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("reminder.deliver");
        trace?.SetTag("jarvis.reminder.id", reminder.ReminderId);
        activity.Heartbeat(reminder.ReminderId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReminderRepository>();
        await repository.CompleteAndNotifyAsync(reminder, activity.CancellationToken);
    }

    [Activity("DeliverReminderOccurrence")]
    public override async Task<ReminderDeliveryResult> DeliverReminderOccurrenceAsync(ReminderWorkflowInput reminder)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("reminder.deliver");
        trace?.SetTag("jarvis.reminder.id", reminder.ReminderId);
        activity.Heartbeat(reminder.ReminderId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReminderRepository>();
        return await repository.CompleteAndNotifyAsync(reminder, activity.CancellationToken);
    }

    [Activity("FailReminder")]
    public override async Task FailReminderAsync(ReminderWorkflowInput reminder)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("reminder.fail");
        trace?.SetTag("jarvis.reminder.id", reminder.ReminderId);
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IReminderRepository>()
            .MarkScheduleFailedAsync(reminder.ReminderId, activity.CancellationToken);
    }
}
