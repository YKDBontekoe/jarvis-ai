using Jarvis.Agents.Learning;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Learning;
using Jarvis.Application.Settings;

namespace Jarvis.Api.Endpoints;

public sealed record LearningStatusDto(LearningSettings Settings, HeartbeatState State,
    IReadOnlyList<AuditEventDto> Activity);

internal static class LearningEndpoints
{
    private static readonly HashSet<string> LearningTools = ["learning", "skills"];

    public static RouteGroupBuilder MapLearningEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        api.MapGet("/settings/learning", async (IOwnerSettingsStore settings, ICurrentUser currentUser,
                CancellationToken ct) =>
            Results.Ok(await settings.GetAsync<LearningSettings>(currentUser.OwnerId, SettingsSections.Learning, ct)
                       ?? LearningSettings.Default))
            .WithName("GetLearningSettings");

        api.MapPut("/settings/learning", async (LearningSettings request, IOwnerSettingsStore settings,
            IHeartbeatScheduler scheduler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            LearningSettings normalized;
            try { normalized = request.Normalize(); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("learning", exception.Message); }
            await settings.SaveAsync(currentUser.OwnerId, SettingsSections.Learning, normalized, ct);
            try
            {
                if (normalized.HeartbeatEnabled)
                    await scheduler.ScheduleHeartbeatAsync(currentUser.OwnerId, ct);
                else
                    await scheduler.CancelHeartbeatAsync(currentUser.OwnerId, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogInformation(exception,
                    "Heartbeat scheduling for {OwnerId} will be repaired by the worker reconciler.", currentUser.OwnerId);
            }
            return Results.Ok(normalized);
        }).WithName("SaveLearningSettings");

        api.MapGet("/learning/status", async (IOwnerSettingsStore settings, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var ownerId = currentUser.OwnerId;
            var activity = (await audit.ListAsync(ownerId, 200, ct))
                .Where(item => LearningTools.Contains(item.Tool) && item.Action != "heartbeat.ran")
                .Take(30)
                .Select(item => item.ToDto())
                .ToArray();
            return Results.Ok(new LearningStatusDto(
                await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, ct) ?? LearningSettings.Default,
                await settings.GetAsync<HeartbeatState>(ownerId, LearningSections.HeartbeatState, ct) ?? new HeartbeatState(),
                activity));
        }).WithName("GetLearningStatus");

        api.MapPost("/learning/run", async (HeartbeatService heartbeat, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var outcome = await heartbeat.RunAsync(currentUser.OwnerId, ct);
            return Results.Ok(outcome);
        }).WithName("RunHeartbeatNow");

        return api;
    }
}
