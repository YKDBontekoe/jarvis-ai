using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Modes;

namespace Jarvis.Api.Endpoints;

public sealed record ActiveModeRequest(string? Mode, int? Minutes);

public sealed record ModeSettingsRequest(bool? Auto, string? SleepStart, string? SleepEnd);

public sealed record ModeAttachmentRequest(Guid? ProfileId, string? RoutineId);

public sealed record CatchUpDomainRequest(string? Domain, bool? Included);

public sealed record FocusStartRequest(string? Intention, int? Minutes);

public sealed record FocusEndRequest(string? Reflection);

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

        group.MapGet("/context", async (IModeContextService context, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await context.GetAsync(currentUser.OwnerId, ct))).WithName("GetModeContext");

        group.MapGet("/attachments", async (Guid? currentProfileId, IModeAttachmentService attachments,
            ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await attachments.ListAsync(currentUser.OwnerId, currentProfileId, ct))).WithName("ListModeAttachments");

        group.MapGet("/suggestion", async (Guid? currentProfileId, IModeAttachmentService attachments,
            ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await attachments.SuggestAsync(currentUser.OwnerId, currentProfileId, ct))).WithName("GetModeSuggestion");

        group.MapPut("/{mode}/attachment", async (string mode, ModeAttachmentRequest request, IModeAttachmentService attachments,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await attachments.SetAsync(currentUser.OwnerId, mode, request.ProfileId, request.RoutineId, ct);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.");
        }).WithName("SetModeAttachment");

        group.MapGet("/catch-up", async (ICatchUpService catchUp, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await catchUp.GetAsync(currentUser.OwnerId, ct))).WithName("GetCatchUp");

        group.MapPut("/catch-up/domains", async (CatchUpDomainRequest request, ICatchUpService catchUp,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var (view, error) = await catchUp.SetDomainAsync(currentUser.OwnerId, request.Domain ?? "",
                request.Included ?? true, ct);
            if (error is not null) return ApiProblemResults.Validation("domain", error);
            return view is null ? ApiProblemResults.Validation("domain", "Unknown kind of notification.") : Results.Ok(view);
        }).WithName("SetCatchUpDomain");

        group.MapPost("/catch-up/clear", async (ICatchUpService catchUp, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await catchUp.ClearAsync(currentUser.OwnerId, ct);
            return Results.Ok(await catchUp.GetAsync(currentUser.OwnerId, ct));
        }).WithName("ClearCatchUp");

        group.MapGet("/focus", async (IModeContextService context, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await context.RecentAsync(currentUser.OwnerId, ct))).WithName("ListFocusSessions");

        group.MapPost("/focus", async (FocusStartRequest request, IModeContextService context, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await context.StartFocusAsync(currentUser.OwnerId, request.Intention, request.Minutes, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "mode.focus_started", "focus", ct);
            return Respond(result);
        }).WithName("StartFocus");

        group.MapPost("/focus/end", async (FocusEndRequest request, IModeContextService context,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await context.EndFocusAsync(currentUser.OwnerId, request.Reflection, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "mode.focus_ended", "auto", ct);
            return Respond(result);
        }).WithName("EndFocus");

        group.MapPost("/correct", async (IModeContextService context, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await context.CorrectAsync(currentUser.OwnerId, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "mode.corrected", "normal", ct);
            return Respond(result);
        }).WithName("CorrectMode");

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

    private static IResult Respond(ModeOperation<ModeContextCard> result) => result.Succeeded
        ? Results.Ok(result.Value)
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
