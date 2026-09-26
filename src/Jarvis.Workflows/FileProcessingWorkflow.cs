using Jarvis.Application.Files;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class FileProcessingActivityContract
{
    [Activity("ProcessStoredFile")]
    public abstract Task ProcessStoredFileAsync(FileProcessingInput input);
}

[Workflow]
public sealed class FileProcessingWorkflow
{
    [WorkflowRun]
    public Task RunAsync(FileProcessingInput input) => Workflow.ExecuteActivityAsync(
        (FileProcessingActivityContract activities) => activities.ProcessStoredFileAsync(input),
        new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(10),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(3),
                MaximumInterval = TimeSpan.FromMinutes(1),
                BackoffCoefficient = 2,
                MaximumAttempts = 5
            }
        });
}
