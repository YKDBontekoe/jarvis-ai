using Temporalio.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public sealed record ProactiveCaseWorkflowInput(Guid OwnerId, Guid CaseId);
public sealed record ProactiveCheckSchedule(bool Continue, int DelayMinutes);

public abstract class ProactiveCaseActivityContract
{
    [Activity("CheckProactiveCase")]
    public abstract Task<ProactiveCheckSchedule> CheckAsync(ProactiveCaseWorkflowInput input);
}

/// <summary>One durable check at a time; PostgreSQL revisions and leases also protect activity retries.</summary>
[Workflow]
public sealed class ProactiveCaseWorkflow
{
    private bool wake;
    public static string Id(Guid ownerId, Guid caseId) => $"proactive-{ownerId:N}-{caseId:N}";

    [WorkflowSignal]
    public Task WakeAsync() { wake = true; return Task.CompletedTask; }

    [WorkflowRun]
    public async Task RunAsync(ProactiveCaseWorkflowInput input)
    {
        for (var round = 0; round < 100; round++)
        {
            wake = false;
            var schedule = await Workflow.ExecuteActivityAsync(
                (ProactiveCaseActivityContract activities) => activities.CheckAsync(input),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(4),
                    RetryPolicy = new RetryPolicy { MaximumAttempts = 3, InitialInterval = TimeSpan.FromSeconds(10) } });
            if (!schedule.Continue) return;
            await Workflow.WaitConditionAsync(() => wake, TimeSpan.FromMinutes(Math.Clamp(schedule.DelayMinutes, 1, 10080)));
        }
        throw Workflow.CreateContinueAsNewException((ProactiveCaseWorkflow workflow) => workflow.RunAsync(input));
    }
}
