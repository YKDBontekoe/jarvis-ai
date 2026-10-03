using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Missions;
using Jarvis.Domain.Missions;

namespace Jarvis.Api.Endpoints;

public sealed record MissionRequest(string? Goal, string? Title, Guid? ProjectId);

public sealed record StepUpdateRequest(string? Instruction);

public sealed record MissionSummaryDto(Guid Id, string Title, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record MissionStepDto(Guid Id, string Key, int Ordinal, int Stage, string Role, string Title,
    string Instruction, IReadOnlyList<string> DependsOn, string Status, string? TaskStatus, Guid? TaskId,
    string? Result, string? Error);

public sealed record MissionNoteDto(string Key, string Value, string? StepKey);

public sealed record MissionDetailDto(Guid Id, string Title, string Goal, string Status, string? Summary,
    string? FailureReason, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<MissionStepDto> Steps, IReadOnlyList<MissionNoteDto> Notes);

/// <summary>
/// Mission control: plan a big job into a crew of steps, start, pause, cancel, and steer individual steps. Audit
/// entries carry ids only, never goals or results.
/// </summary>
internal static class MissionEndpoints
{
    public static RouteGroupBuilder MapMissionEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/missions");

        group.MapGet("", async (IMissionService missions, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok((await missions.ListAsync(currentUser.OwnerId, ct))
                .Select(x => new MissionSummaryDto(x.Id, x.Title, x.Status, x.CreatedAt, x.CompletedAt))
                .ToArray())).WithName("ListMissions");

        group.MapGet("/{id:guid}", async (Guid id, IMissionService missions, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var detail = await missions.GetAsync(id, currentUser.OwnerId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(ToDto(detail));
        }).WithName("GetMission");

        group.MapPost("", async (MissionRequest request, IMissionService missions, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await missions.CreateAsync(currentUser.OwnerId, request.Goal, request.Title,
                request.ProjectId, ct);
            if (!result.Succeeded) return Respond(result);
            await AuditAsync(audit, logger, currentUser, "mission.planned", result.Value!.Mission.Id, ct);
            return Results.Created($"/api/v1/missions/{result.Value.Mission.Id}", ToDto(result.Value));
        }).WithName("PlanMission");

        group.MapPost("/{id:guid}/start", async (Guid id, IMissionService missions, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await missions.StartAsync(id, currentUser.OwnerId, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "mission.started", id, ct);
            return Respond(result);
        }).WithName("StartMission");

        group.MapPost("/{id:guid}/pause", async (Guid id, IMissionService missions, ICurrentUser currentUser,
            CancellationToken ct) => Respond(await missions.PauseAsync(id, currentUser.OwnerId, ct)))
            .WithName("PauseMission");

        group.MapPost("/{id:guid}/resume", async (Guid id, IMissionService missions, ICurrentUser currentUser,
            CancellationToken ct) => Respond(await missions.ResumeAsync(id, currentUser.OwnerId, ct)))
            .WithName("ResumeMission");

        group.MapPost("/{id:guid}/cancel", async (Guid id, IMissionService missions, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await missions.CancelAsync(id, currentUser.OwnerId, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "mission.cancelled", id, ct);
            return Respond(result);
        }).WithName("CancelMission");

        group.MapDelete("/{id:guid}", async (Guid id, IMissionService missions, ICurrentUser currentUser,
            CancellationToken ct) =>
            await missions.DeleteAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteMission");

        var steps = group.MapGroup("/steps");

        steps.MapPut("/{stepId:guid}", async (Guid stepId, StepUpdateRequest request, IMissionService missions,
            ICurrentUser currentUser, CancellationToken ct) =>
            Respond(await missions.UpdateStepAsync(stepId, currentUser.OwnerId, request.Instruction, ct)))
            .WithName("UpdateMissionStep");

        steps.MapPost("/{stepId:guid}/skip", async (Guid stepId, IMissionService missions, ICurrentUser currentUser,
            CancellationToken ct) => Respond(await missions.SkipStepAsync(stepId, currentUser.OwnerId, ct)))
            .WithName("SkipMissionStep");

        steps.MapPost("/{stepId:guid}/retry", async (Guid stepId, IMissionService missions, ICurrentUser currentUser,
            CancellationToken ct) => Respond(await missions.RetryStepAsync(stepId, currentUser.OwnerId, ct)))
            .WithName("RetryMissionStep");

        return api;
    }

    private static IResult Respond(MissionOperation<MissionDetail> result) => result.Failure switch
    {
        MissionFailure.None => Results.Ok(ToDto(result.Value!)),
        MissionFailure.NotFound => Results.NotFound(),
        MissionFailure.Conflict => Results.Problem(result.Message, statusCode: StatusCodes.Status409Conflict),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    internal static MissionDetailDto ToDto(MissionDetail detail)
    {
        var stages = MissionPlanning.Stages(detail.Steps);
        return new MissionDetailDto(detail.Mission.Id, detail.Mission.Title, detail.Mission.Goal,
            detail.Mission.Status, detail.Mission.Summary, detail.Mission.FailureReason, detail.Mission.CreatedAt,
            detail.Mission.StartedAt, detail.Mission.CompletedAt,
            detail.Steps.Select(x => new MissionStepDto(x.Id, x.Key, x.Ordinal, stages.GetValueOrDefault(x.Key),
                x.Role, x.Title, x.Instruction, x.DependsOn, x.Status,
                x.TaskId is { } task ? detail.TaskStatuses.GetValueOrDefault(task) : null, x.TaskId, x.Result,
                x.Error)).ToArray(),
            detail.Notes.Select(x => new MissionNoteDto(x.Key, x.Value, x.StepKey)).ToArray());
    }

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid resourceId, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "missions", action, "low", true, null,
            JsonSerializer.Serialize(new { resourceId, source = "app" }), ct);
}
