using Jarvis.Application.Workflows;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class ReminderActivityContract
{
    [Activity("DeliverReminder")]
    public abstract Task DeliverReminderAsync(ReminderWorkflowInput reminder);

    [Activity("FailReminder")]
    public abstract Task FailReminderAsync(ReminderWorkflowInput reminder);
}

[Workflow]
public sealed class ReminderWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(ReminderWorkflowInput reminder)
    {
        try
        {
            var delay = reminder.DueAt - Workflow.UtcNow;
            if (delay > TimeSpan.Zero) await Workflow.DelayAsync(delay);

            await Workflow.ExecuteActivityAsync(
                (ReminderActivityContract activities) => activities.DeliverReminderAsync(reminder),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(1),
                    RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 8 }
                });
        }
        catch (CanceledFailureException)
        {
            throw;
        }
        catch (Exception)
        {
            await Workflow.ExecuteActivityAsync(
                (ReminderActivityContract activities) => activities.FailReminderAsync(reminder),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(1),
                    RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 8 }
                });
            throw;
        }
    }
}
