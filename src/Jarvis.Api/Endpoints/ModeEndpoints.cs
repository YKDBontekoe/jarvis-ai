using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Modes;

namespace Jarvis.Api.Endpoints;

public sealed record ActiveModeRequest(string? Mode, int? Minutes);

public sealed record ModeSettingsRequest(bool? Auto, string? SleepStart, string? SleepEnd);

public sealed record ModePolicyRequest(string? Notifications, string? Tone);

public sealed record ModeDto(string Id, string Label, string Description, string Notifications, string? Tone,
    bool Customised);

public sealed record ModeStateDto(string Mode, string Label, string Source, string Reason, DateTimeOffset? Until,
    string Notifications, bool Auto, string SleepStart, string SleepEnd, IReadOnlyList<ModeDto> Modes);

/// <summary>
/// Context modes: what Jarvis is doing for the owner right now (focus, meeting, sleep, …), decided by hand or from
/// the clock and calendar, and how each mode changes pushes and replies.
/// </summary>
internal static class ModeEndpoints
{
    public static RouteGroupBuilder MapModeEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/modes");

        group.MapGet("", async (IModeService modes, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(ToDto(await modes.GetStateAsync(currentUser.OwnerId, ct)))).WithName("GetModes");

        group.MapPut("/active", async (ActiveModeRequest request, IModeService modes, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await modes.SetModeAsync(currentUser.OwnerId, request.Mode, request.Minutes, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "mode.switched", request.Mode, ct);
            return Respond(result);
        }).WithName("SetActiveMode");

        group.MapPut("/settings", async (ModeSettingsRequest request, IModeService modes, ICurrentUser currentUser,
            CancellationToken ct) =>
            Respond(await modes.SaveSettingsAsync(currentUser.OwnerId, request.Auto, request.SleepStart,
                request.SleepEnd, ct))).WithName("SaveModeSettings");

        group.MapPut("/{mode}/policy", async (string mode, ModePolicyRequest request, IModeService modes,
            ICurrentUser currentUser, CancellationToken ct) =>
            Respond(await modes.SavePolicyAsync(currentUser.OwnerId, mode, request.Notifications, request.Tone, ct)))
            .WithName("SaveModePolicy");

        group.MapDelete("/{mode}/policy", async (string mode, IModeService modes, ICurrentUser currentUser,
            CancellationToken ct) =>
            Respond(await modes.ResetPolicyAsync(currentUser.OwnerId, mode, ct))).WithName("ResetModePolicy");

        return api;
    }

    private static IResult Respond(ModeOperation<ModeState> result) => result.Succeeded
        ? Results.Ok(ToDto(result.Value!))
        : ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.");

    internal static ModeStateDto ToDto(ModeState state)
    {
        var current = state.Modes.First(x => x.Definition.Id == state.Current.Mode).Definition;
        return new ModeStateDto(current.Id, current.Label, state.Current.Source, state.Current.Reason,
            state.Current.Until, state.Policy.Notifications, state.Auto, state.SleepStart, state.SleepEnd,
            state.Modes.Select(x => new ModeDto(x.Definition.Id, x.Definition.Label, x.Definition.Description,
                x.Definition.Policy.Notifications, x.Definition.Policy.Tone, x.Customised)).ToArray());
    }

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        string? mode, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "modes", action, "low", true, null,
            JsonSerializer.Serialize(new { mode = mode ?? "auto", source = "app" }), ct);
}
