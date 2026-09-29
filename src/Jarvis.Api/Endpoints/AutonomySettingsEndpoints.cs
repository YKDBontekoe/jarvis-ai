using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Settings;

namespace Jarvis.Api.Endpoints;

public sealed record SaveAutonomySettingsRequest(string? Mode);

internal static class AutonomySettingsEndpoints
{
    public static RouteGroupBuilder MapAutonomySettingsEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/settings/autonomy");

        group.MapGet("", async (IOwnerSettingsStore settings, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await settings.GetAsync<AutonomySettings>(currentUser.OwnerId, SettingsSections.Autonomy, ct)
                       ?? AutonomySettings.Default))
            .WithName("GetAutonomySettings");

        group.MapPut("", async (SaveAutonomySettingsRequest request, IOwnerSettingsStore settings,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            AutonomySettings normalized;
            try { normalized = new AutonomySettings(request.Mode ?? AutonomySettings.Ask).Normalize(); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("mode", exception.Message); }
            await settings.SaveAsync(currentUser.OwnerId, SettingsSections.Autonomy, normalized, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "settings",
                "autonomy.changed", "high", true, null, JsonSerializer.Serialize(new { mode = normalized.Mode }), ct);
            return Results.Ok(normalized);
        }).WithName("SaveAutonomySettings");

        return api;
    }
}
