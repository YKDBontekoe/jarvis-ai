using Jarvis.Application.Learning;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class AssistantDreamingActivityContract
{
    [Activity("RunAssistantDreaming")]
    public abstract Task<DreamingRunResult> RunAsync(DreamingWorkflowInput input);

    /// <summary>Builds at most one draft app for a capability Jarvis keeps lacking. Returns how many were built.</summary>
    [Activity("RunCapabilityEvolution")]
    public abstract Task<int> EvolveAsync(DreamingWorkflowInput input);
}

/// <summary>Durable per-owner dreaming sweep that consolidates memory around the configured local hour.</summary>
[Workflow]
public sealed class AssistantDreamingWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(DreamingWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(15),
            HeartbeatTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(15),
                MaximumInterval = TimeSpan.FromMinutes(5),
                MaximumAttempts = 3
            }
        };

        while (true)
        {
            DreamingRunResult result;
            try
            {
                result = await Workflow.ExecuteActivityAsync(
                    (AssistantDreamingActivityContract activities) => activities.RunAsync(input), options);
            }
            catch (ActivityFailureException)
            {
                result = new DreamingRunResult(true, 60);
            }
            if (!result.Continue) return;
            if (Workflow.Patched("capability-evolution"))
            {
                try
                {
                    // Writing and testing code gets its own budget and is never retried: a failed night just waits for the next.
                    await Workflow.ExecuteActivityAsync(
                        (AssistantDreamingActivityContract activities) => activities.EvolveAsync(input),
                        new ActivityOptions
                        {
                            StartToCloseTimeout = TimeSpan.FromMinutes(20),
                            HeartbeatTimeout = TimeSpan.FromMinutes(2),
                            RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 1 }
                        });
                }
                catch (ActivityFailureException)
                {
                }
            }
            await Workflow.DelayAsync(TimeSpan.FromMinutes(Math.Clamp(result.NextRunMinutes, 15, 1_440)));
            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (AssistantDreamingWorkflow workflow) => workflow.RunAsync(input));
        }
    }
}
