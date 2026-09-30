using Jarvis.Application.Automations;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class AutomationRunActivityContract
{
    [Activity("ExecuteAutomationRun")]
    public abstract Task<AutomationRunActivityResult> ExecuteAsync(AutomationRunWorkflowInput input);

    [Activity("CompleteAutomationRunAfterApproval")]
    public abstract Task<AutomationRunActivityResult> CompleteAfterApprovalAsync(AutomationRunWorkflowInput input);

    [Activity("CloseAutomationRun")]
    public abstract Task CloseRunAsync(AutomationRunCloseInput input);
}

[Workflow]
public sealed class AutomationRunWorkflow
{
    private bool? _approvalGranted;

    [WorkflowSignal]
    public Task ResolveApprovalAsync(bool approved)
    {
        _approvalGranted = approved;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task RunAsync(AutomationRunWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(20),
            HeartbeatTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(2),
                MaximumInterval = TimeSpan.FromMinutes(2),
                MaximumAttempts = 5
            }
        };

        try
        {
            var result = await Workflow.ExecuteActivityAsync(
                (AutomationRunActivityContract activities) => activities.ExecuteAsync(input), options);
            if (result.WaitingApproval)
            {
                var decided = await Workflow.WaitConditionAsync(() => _approvalGranted is not null,
                    AutomationApprovals.DecisionWindow);
                if (!decided && Workflow.Patched("automation-run-close"))
                {
                    await CloseAsync(input, "Nobody decided on the approval in time.");
                    return;
                }
                if (_approvalGranted != true) return;
                await Workflow.ExecuteActivityAsync(
                    (AutomationRunActivityContract activities) => activities.CompleteAfterApprovalAsync(input),
                    options);
            }
        }
        catch (CanceledFailureException)
        {
            throw;
        }
        catch (ActivityFailureException) when (Workflow.Patched("automation-run-close"))
        {
            // Every retry failed. Close the run so it stops counting against the owner's concurrent-run limit.
            await CloseAsync(input, "Jarvis could not finish this run.");
            throw;
        }
    }

    private static Task CloseAsync(AutomationRunWorkflowInput input, string reason) =>
        Workflow.ExecuteActivityAsync(
            (AutomationRunActivityContract activities) => activities.CloseRunAsync(
                new AutomationRunCloseInput(input.RunId, input.OwnerId, reason)),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(1),
                RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 8 }
            });
}
