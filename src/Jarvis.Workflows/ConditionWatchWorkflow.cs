using Jarvis.Application.Workflows;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class ConditionWatchActivityContract
{
    [Activity("CheckConditionWatch")]
    public abstract Task<ConditionWatchCheckResult> CheckAsync(ConditionWatchWorkflowInput input);

    [Activity("FailConditionWatch")]
    public abstract Task FailAsync(ConditionWatchWorkflowInput input);
}

[Workflow]
public sealed class ConditionWatchWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(ConditionWatchWorkflowInput input)
    {
        var activityOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromSeconds(45),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(5),
                MaximumInterval = TimeSpan.FromMinutes(5),
                MaximumAttempts = 8
            }
        };

        try
        {
            while (true)
            {
                var result = await Workflow.ExecuteActivityAsync(
                    (ConditionWatchActivityContract activities) => activities.CheckAsync(input), activityOptions);
                if (!result.Continue) return;
                await Workflow.DelayAsync(TimeSpan.FromMinutes(result.IntervalMinutes));
                if (Workflow.ContinueAsNewSuggested)
                    throw Workflow.CreateContinueAsNewException(
                        (ConditionWatchWorkflow workflow) => workflow.RunAsync(input));
            }
        }
        catch (ContinueAsNewException)
        {
            throw;
        }
        catch (CanceledFailureException)
        {
            throw;
        }
        catch (Exception)
        {
            await Workflow.ExecuteActivityAsync(
                (ConditionWatchActivityContract activities) => activities.FailAsync(input),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(1),
                    RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 8 }
                });
            throw;
        }
    }
}
