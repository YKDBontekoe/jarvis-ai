using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public sealed record AgentReactionWorkflowInput(Guid OwnerId);

public sealed record AgentReactionActivityInput(Guid OwnerId, IReadOnlyList<Guid> EventIds);

public static class AgentReactionWorkflowIds
{
    public static string For(Guid ownerId) => $"agent-reaction-{ownerId:N}";
}

public abstract class AgentReactionActivityContract
{
    [Activity("RunAgentReaction")]
    public abstract Task<Guid?> RunAsync(AgentReactionActivityInput input);
}

/// <summary>
/// One per owner while events arrive. Each event is a signal; the workflow waits a short window so related events
/// (a watch firing and the task it broke) become one reaction, then starts it, and stops once a window passes quietly.
/// </summary>
[Workflow]
public sealed class AgentReactionWorkflow
{
    public static readonly TimeSpan BatchWindow = TimeSpan.FromMinutes(2);
    private const int MaxPending = 50;
    private const int MaxRounds = 20;
    private readonly List<Guid> _pending = [];

    [WorkflowSignal]
    public Task EventAsync(Guid eventId)
    {
        if (_pending.Count < MaxPending && !_pending.Contains(eventId)) _pending.Add(eventId);
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task RunAsync(AgentReactionWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(10),
                MaximumInterval = TimeSpan.FromMinutes(1),
                MaximumAttempts = 3
            }
        };

        for (var round = 0; round < MaxRounds; round++)
        {
            await Workflow.DelayAsync(BatchWindow);
            if (_pending.Count == 0) return;
            var batch = _pending.ToArray();
            _pending.Clear();
            try
            {
                await Workflow.ExecuteActivityAsync(
                    (AgentReactionActivityContract activities) => activities.RunAsync(
                        new AgentReactionActivityInput(input.OwnerId, batch)), options);
            }
            catch (ActivityFailureException)
            {
                // The events stay in the owner's feed and situation; a failed reaction is not retried forever.
            }
        }

        if (_pending.Count > 0)
            throw Workflow.CreateContinueAsNewException((AgentReactionWorkflow workflow) => workflow.RunAsync(input));
    }
}
