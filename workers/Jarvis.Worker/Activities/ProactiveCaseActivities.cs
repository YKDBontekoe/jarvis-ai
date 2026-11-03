using Jarvis.Application.Proactivity;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class ProactiveCaseActivities(IServiceScopeFactory scopes) : ProactiveCaseActivityContract
{
    [Activity("CheckProactiveCase")]
    public override async Task<ProactiveCheckSchedule> CheckAsync(ProactiveCaseWorkflowInput input)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<WorkerCurrentUser>().SetOwner(input.OwnerId);
        var value = await scope.ServiceProvider.GetRequiredService<IProactiveService>()
            .CheckAsync(input.OwnerId, input.CaseId, ActivityExecutionContext.Current.CancellationToken);
        return new(value is { Enabled: true, Terminal: false, NextCheckAt: not null }, value is null ? 15 :
            Math.Max(15, (int)Math.Ceiling(((value.NextCheckAt ?? DateTimeOffset.UtcNow.AddMinutes(15)) - DateTimeOffset.UtcNow).TotalMinutes)));
    }
}
