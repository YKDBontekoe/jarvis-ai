using Jarvis.Application.Habits;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class HabitCheckInActivityContract
{
    [Activity("ResolveHabitCheckIn")]
    public abstract Task<HabitCheckInSchedule> ResolveAsync(HabitCheckInWorkflowInput input);

    [Activity("DeliverHabitCheckIn")]
    public abstract Task<bool> DeliverAsync(HabitCheckInDelivery input);
}

/// <summary>
/// Per-owner evening question about habits. Each round reads the time and zone again, so changing them needs no
/// restart; the workflow ends when the question is turned off or no active habits remain.
/// </summary>
[Workflow]
public sealed class HabitCheckInWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(HabitCheckInWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(1),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(5),
                MaximumInterval = TimeSpan.FromMinutes(1),
                MaximumAttempts = 5
            }
        };

        while (true)
        {
            try
            {
                var schedule = await Workflow.ExecuteActivityAsync(
                    (HabitCheckInActivityContract activities) => activities.ResolveAsync(input), options);
                if (!schedule.Active) return;
                var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
                var delay = schedule.FireAt - now;
                if (delay > TimeSpan.Zero) await Workflow.DelayAsync(delay);
                var keepGoing = await Workflow.ExecuteActivityAsync(
                    (HabitCheckInActivityContract activities) => activities.DeliverAsync(
                        new HabitCheckInDelivery(input.OwnerId, schedule.LocalDate)), options);
                if (!keepGoing) return;
            }
            catch (ActivityFailureException)
            {
                // Data or push delivery is briefly unavailable; try again later instead of ending the schedule.
                await Workflow.DelayAsync(TimeSpan.FromMinutes(15));
            }

            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (HabitCheckInWorkflow workflow) => workflow.RunAsync(input));
        }
    }
}
