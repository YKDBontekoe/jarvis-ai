using Jarvis.Application.Learning;
using Jarvis.Application.Routines;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Worker.Activities;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Learning;

internal sealed class AssistantDreamingActivities(IServiceScopeFactory scopeFactory) : AssistantDreamingActivityContract
{
    [Activity("RunAssistantDreaming")]
    public override async Task<DreamingRunResult> RunAsync(DreamingWorkflowInput input)
    {
        var cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("dreaming.run");
        using var heartbeat = new ActivityHeartbeat(input.OwnerId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var settings = await services.GetRequiredService<IOwnerSettingsStore>()
            .GetAsync<LearningSettings>(input.OwnerId, SettingsSections.Learning, cancellationToken)
            ?? LearningSettings.Default;
        if (!settings.DreamingEnabled)
            return new DreamingRunResult(false, 0);
        services.GetRequiredService<WorkerCurrentUser>().SetOwner(input.OwnerId);
        await services.GetRequiredService<Jarvis.Agents.Learning.DreamingService>()
            .SweepAsync(input.OwnerId, force: false, cancellationToken);
        try
        {
            // Spotting routines is a bonus of the nightly pass; it never fails the dream.
            await services.GetRequiredService<IRoutineSuggestionService>()
                .RefreshAsync(input.OwnerId, force: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<AssistantDreamingActivities>>()
                .LogWarning(exception, "Routine suggestions could not be refreshed after dreaming.");
        }
        var briefing = await services.GetRequiredService<IDailyBriefingRepository>()
            .GetAsync(input.OwnerId, cancellationToken);
        var minutes = DreamingClock.MinutesUntilNext(DateTimeOffset.UtcNow,
            settings.DreamingHour, briefing?.TimeZoneId);
        return new DreamingRunResult(true, minutes);
    }
}
