using Jarvis.Application.Learning;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class AssistantHeartbeatActivityContract
{
    [Activity("RunAssistantHeartbeat")]
    public abstract Task<HeartbeatRunResult> RunAsync(HeartbeatWorkflowInput input);
}

/// <summary>Durable per-owner heartbeat that reflects, learns, and checks in until the owner turns it off.</summary>
[Workflow]
public sealed class AssistantHeartbeatWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(HeartbeatWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(10),
            HeartbeatTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(10),
                MaximumInterval = TimeSpan.FromMinutes(2),
                MaximumAttempts = 3
            }
        };

        while (true)
        {
            HeartbeatRunResult result;
            try
            {
                result = await Workflow.ExecuteActivityAsync(
                    (AssistantHeartbeatActivityContract activities) => activities.RunAsync(input), options);
            }
            catch (ActivityFailureException)
            {
                result = new HeartbeatRunResult(true, 30);
            }
            if (!result.Continue) return;
            await Workflow.DelayAsync(TimeSpan.FromMinutes(Math.Clamp(result.NextRunMinutes, 15, 1_440)));
            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (AssistantHeartbeatWorkflow workflow) => workflow.RunAsync(input));
        }
    }
}
