using Jarvis.Application.Reviews;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class WeeklyReviewActivityContract
{
    [Activity("ResolveWeeklyReviewSchedule")]
    public abstract Task<WeeklyReviewSchedule> ResolveScheduleAsync(WeeklyReviewWorkflowInput input);

    [Activity("DeliverWeeklyReview")]
    public abstract Task<bool> DeliverAsync(WeeklyReviewActivityInput input);
}

/// <summary>
/// Durable per-owner loop that sends the weekly look-back on Sunday evening in the owner's time zone. It re-reads
/// the settings at least twice a day and right away when signalled, so a changed time or zone takes effect without
/// restarting the workflow. Turning the review off ends the run at the next check.
/// </summary>
[Workflow]
public sealed class WeeklyReviewWorkflow
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);
    private bool _settingsChanged;

    [WorkflowSignal]
    public Task SettingsChangedAsync()
    {
        _settingsChanged = true;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task RunAsync(WeeklyReviewWorkflowInput input)
    {
        var resolveOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(1),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(5),
                MaximumInterval = TimeSpan.FromMinutes(1),
                MaximumAttempts = 5
            }
        };
        var deliverOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(3),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(15),
                MaximumInterval = TimeSpan.FromMinutes(2),
                MaximumAttempts = 4
            }
        };

        while (true)
        {
            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (WeeklyReviewWorkflow workflow) => workflow.RunAsync(input));

            _settingsChanged = false;
            WeeklyReviewSchedule schedule;
            try
            {
                schedule = await Workflow.ExecuteActivityAsync(
                    (WeeklyReviewActivityContract activities) => activities.ResolveScheduleAsync(input),
                    resolveOptions);
            }
            catch (ActivityFailureException)
            {
                await Workflow.DelayAsync(RetryDelay);
                continue;
            }

            if (!schedule.Continue) return;
            var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
            var wait = schedule.FireAt - now;
            if (wait > TimeSpan.Zero)
            {
                await Workflow.WaitConditionAsync(() => _settingsChanged, wait < MaxSleep ? wait : MaxSleep);
                continue;
            }

            try
            {
                var keepGoing = await Workflow.ExecuteActivityAsync(
                    (WeeklyReviewActivityContract activities) => activities.DeliverAsync(
                        new WeeklyReviewActivityInput(input.OwnerId, schedule.WeekStart)), deliverOptions);
                if (!keepGoing) return;
            }
            catch (ActivityFailureException)
            {
                // The catch-up window in the schedule stops retrying a week that keeps failing.
                await Workflow.DelayAsync(RetryDelay);
            }
        }
    }
}
