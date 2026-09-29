using Jarvis.Application.Files;
using Jarvis.Worker.Files;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class FileProcessingActivities(IServiceScopeFactory scopeFactory, StoredFileProcessor processor)
    : FileProcessingActivityContract
{
    [Activity("ProcessStoredFile")]
    public override async Task ProcessStoredFileAsync(FileProcessingInput input)
    {
        var activity = ActivityExecutionContext.Current;
        var cancellationToken = activity.CancellationToken;
        using var heartbeat = new ActivityHeartbeat(input.FileId);
        await using var scope = scopeFactory.CreateAsyncScope();
        await processor.ProcessAsync(scope.ServiceProvider, input, activity.Info.Attempt, cancellationToken);
    }
}
