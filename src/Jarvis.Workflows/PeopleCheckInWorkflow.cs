using Jarvis.Application.People;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class PeopleCheckInActivityContract
{
    [Activity("RunPeopleCheckIn")]
    public abstract Task<PeopleCheckInResult> RunAsync(PeopleCheckInInput input);
}

/// <summary>
/// Durable per-owner daily check-in for birthdays and keep-in-touch nudges. The activity decides when to look
/// again (the next local 09:00); the workflow ends once the owner has nobody with a birthday or check-in cadence,
/// and the worker's reconciler starts it again when someone is added.
/// </summary>
[Workflow]
public sealed class PeopleCheckInWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(PeopleCheckInInput input)
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
            TimeSpan delay;
            try
            {
                var result = await Workflow.ExecuteActivityAsync(
                    (PeopleCheckInActivityContract activities) => activities.RunAsync(input), options);
                if (!result.Continue) return;
                var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
                delay = result.NextRunAt - now;
            }
            catch (ActivityFailureException)
            {
                // Keep the daily rhythm alive while the database or push delivery is unavailable.
                delay = TimeSpan.FromMinutes(15);
            }

            await Workflow.DelayAsync(delay < TimeSpan.FromMinutes(1)
                ? TimeSpan.FromMinutes(1)
                : delay > TimeSpan.FromDays(2) ? TimeSpan.FromDays(2) : delay);
            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (PeopleCheckInWorkflow workflow) => workflow.RunAsync(input));
        }
    }
}
