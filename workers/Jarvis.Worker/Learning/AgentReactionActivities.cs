using Jarvis.Application.Events;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Learning;

internal sealed class AgentReactionActivities(IServiceScopeFactory scopeFactory) : AgentReactionActivityContract
{
    [Activity("RunAgentReaction")]
    public override async Task<Guid?> RunAsync(AgentReactionActivityInput input)
    {
        var cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("agent_reaction.run");
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<WorkerCurrentUser>().SetOwner(input.OwnerId);
        var taskId = await services.GetRequiredService<AgentReactionRunner>()
            .RunAsync(input.OwnerId, input.EventIds, cancellationToken);
        trace?.SetTag("jarvis.task.id", taskId);
        return taskId;
    }
}
