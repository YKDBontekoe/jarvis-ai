using Jarvis.Application.People;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class PeopleCheckInActivities(IServiceScopeFactory scopeFactory) : PeopleCheckInActivityContract
{
    [Activity("RunPeopleCheckIn")]
    public override async Task<PeopleCheckInResult> RunAsync(PeopleCheckInInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("people.check_in");
        trace?.SetTag("jarvis.owner.id", input.OwnerId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPeopleCheckInService>()
            .RunAsync(input.OwnerId, activity.CancellationToken);
        trace?.SetTag("jarvis.people.birthdays", result.BirthdaysNotified);
        trace?.SetTag("jarvis.people.check_ins", result.CheckInsNotified);
        return result;
    }
}
