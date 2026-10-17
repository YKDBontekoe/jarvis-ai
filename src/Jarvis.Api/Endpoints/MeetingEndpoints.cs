using Jarvis.Application.Conversations;
using Jarvis.Application.Meetings;

namespace Jarvis.Api.Endpoints;

public sealed record MeetingRequest(string? Title, DateTimeOffset? StartsAt, Guid? ProjectId, string? Purpose,
    string? Template, IReadOnlyList<MeetingParticipant>? Participants);

public sealed record MeetingEditRequest(string? Title, DateTimeOffset? StartsAt, string? Purpose, string? PrivateNotes,
    IReadOnlyList<AgendaItem>? Agenda);

public sealed record DebriefRequest(string? Text);

public sealed record AcceptCandidateRequest(string? Description, string? Counterparty, DateOnly? DueOn, bool? ClearDue,
    double? Probability, DateOnly? ReviewOn);

/// <summary>
/// Meetings: a prep pack built from the owner's own records, a debrief that proposes decisions and promises, and a
/// follow-up draft. Nothing is accepted, created or sent until the owner says so.
/// </summary>
internal static class MeetingEndpoints
{
    public static RouteGroupBuilder MapMeetingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/meetings").WithTags("Meetings");

        group.MapGet("", async (IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await meetings.ListAsync(user.OwnerId, ct))).WithName("ListMeetings");

        group.MapGet("/events", async (int? days, IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await meetings.UpcomingEventsAsync(user.OwnerId, days ?? 7, ct))).WithName("ListMeetingEvents");

        group.MapPost("", async (MeetingRequest request, IMeetingService meetings, ICurrentUser user,
            CancellationToken ct) =>
        {
            var created = await meetings.CreateAsync(user.OwnerId,
                new MeetingInput(request.Title, request.StartsAt, request.ProjectId, request.Purpose, request.Template,
                    request.Participants), ct);
            return created.Succeeded ? Results.Created($"/api/v1/meetings/{created.Value!.Id}", created.Value) : Failure(created);
        }).WithName("CreateMeeting");

        group.MapGet("/{id:guid}", async (Guid id, IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
            await meetings.GetAsync(id, user.OwnerId, ct) is { } meeting ? Results.Ok(meeting) : Results.NotFound())
            .WithName("GetMeeting");

        group.MapPut("/{id:guid}", async (Guid id, MeetingEditRequest request, IMeetingService meetings,
            ICurrentUser user, CancellationToken ct) =>
        {
            var updated = await meetings.UpdateAsync(id, user.OwnerId,
                new MeetingEdit(request.Title, request.StartsAt, request.Purpose, request.PrivateNotes, request.Agenda), ct);
            return updated.Succeeded ? Results.Ok(updated.Value) : Failure(updated);
        }).WithName("UpdateMeeting");

        group.MapDelete("/{id:guid}", async (Guid id, IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
            await meetings.DeleteAsync(id, user.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteMeeting");

        group.MapPost("/{id:guid}/prep", async (Guid id, IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
        {
            var prepared = await meetings.PrepareAsync(id, user.OwnerId, ct);
            return prepared.Succeeded ? Results.Ok(prepared.Value) : Failure(prepared);
        }).WithName("PrepareMeeting");

        group.MapPost("/{id:guid}/debrief", async (Guid id, DebriefRequest request, IMeetingService meetings,
            ICurrentUser user, CancellationToken ct) =>
        {
            var debriefed = await meetings.DebriefAsync(id, user.OwnerId, request.Text ?? string.Empty, ct);
            return debriefed.Succeeded ? Results.Ok(debriefed.Value) : Failure(debriefed);
        }).WithName("DebriefMeeting");

        group.MapPost("/{id:guid}/candidates/{candidateId}/accept", async (Guid id, string candidateId,
            AcceptCandidateRequest request, IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
        {
            var accepted = await meetings.AcceptAsync(id, user.OwnerId, candidateId,
                new CandidateAcceptance(request.Description, request.Counterparty, request.DueOn,
                    request.ClearDue == true, request.Probability, request.ReviewOn), ct);
            return accepted.Succeeded ? Results.Ok(accepted.Value) : Failure(accepted);
        }).WithName("AcceptMeetingCandidate");

        group.MapPost("/{id:guid}/candidates/{candidateId}/dismiss", async (Guid id, string candidateId,
            IMeetingService meetings, ICurrentUser user, CancellationToken ct) =>
        {
            var dismissed = await meetings.DismissAsync(id, user.OwnerId, candidateId, ct);
            return dismissed.Succeeded ? Results.Ok(dismissed.Value) : Failure(dismissed);
        }).WithName("DismissMeetingCandidate");

        group.MapPost("/{id:guid}/followup", async (Guid id, IMeetingService meetings, ICurrentUser user,
            CancellationToken ct) =>
        {
            var draft = await meetings.FollowUpAsync(id, user.OwnerId, ct);
            return draft.Succeeded ? Results.Ok(draft.Value) : Failure(draft);
        }).WithName("DraftMeetingFollowUp");

        return api;
    }

    private static IResult Failure<T>(MeetingOperation<T> operation) => operation.Failure switch
    {
        MeetingFailure.NotFound => Results.NotFound(),
        MeetingFailure.Conflict => Results.Conflict(new { message = operation.Message }),
        _ => EndpointHelpers.Invalid(operation.Field ?? "request", operation.Message ?? "The request is invalid.")
    };
}
