using Jarvis.Application.Workflows;
using Temporalio.Exceptions;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class JarvisTaskActivityContract
{
    [Activity("RunJarvisTask")]
    public abstract Task<bool> RunTaskAsync(JarvisTaskWorkflowInput input);

    [Activity("CompleteApprovedJarvisTask")]
    public abstract Task CompleteApprovedTaskAsync(JarvisTaskApprovalInput input);

    [Activity("FailJarvisTask")]
    public abstract Task FailTaskAsync(JarvisTaskWorkflowInput input);
}

[Workflow]
public sealed class JarvisTaskWorkflow
{
    private string? _approvalSummary;

    [WorkflowSignal]
    public Task ResolveApprovalAsync(string summary)
    {
        _approvalSummary = summary;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task RunAsync(JarvisTaskWorkflowInput input)
    {
        try
        {
            var waitingForApproval = await Workflow.ExecuteActivityAsync(
                (JarvisTaskActivityContract activities) => activities.RunTaskAsync(input),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(15),
                    RetryPolicy = new Temporalio.Common.RetryPolicy
                    {
                        InitialInterval = TimeSpan.FromSeconds(2),
                        MaximumInterval = TimeSpan.FromSeconds(30),
                        MaximumAttempts = 3
                    }
                });

            if (!waitingForApproval) return;
            await Workflow.WaitConditionAsync(() => _approvalSummary is not null);
            await Workflow.ExecuteActivityAsync(
                (JarvisTaskActivityContract activities) => activities.CompleteApprovedTaskAsync(
                    new JarvisTaskApprovalInput(input.TaskId, _approvalSummary!)),
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
                (JarvisTaskActivityContract activities) => activities.FailTaskAsync(input),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(1),
                    RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 3 }
                });
            throw;
        }
    }
}
