using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Inbox;
using Jarvis.Domain.Inbox;

namespace Jarvis.Api.Endpoints;

public sealed record InboxThreadDto(Guid Id, string Source, string Title, string? Counterparty, string State,
    int Priority, string? Summary, string? SuggestedReply, string? LastMessagePreview, bool LastFromMe,
    DateTimeOffset? LastMessageAt, DateTimeOffset? SnoozedUntil, DateTimeOffset? TriagedAt);

public sealed record InboxResponse(IReadOnlyList<InboxThreadDto> Threads, IReadOnlyDictionary<string, int> Counts,
    CommitmentSummary Commitments);

public sealed record InboxStateRequest(string? State);

public sealed record InboxSnoozeRequest(DateTimeOffset? Until);

public sealed record InboxTriageDto(InboxThreadDto Thread, IReadOnlyList<CommitmentDto> Suggested);

public sealed record CommitmentDto(Guid Id, string Direction, string Counterparty, string Description,
    DateOnly? DueOn, string Status, bool Suggested, string Source, Guid? InboxThreadId, bool HasReminder,
    DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt);

public sealed record CommitmentRequest(string? Direction, string? Counterparty, string? Description, DateOnly? DueOn);

public sealed record CommitmentUpdateRequest(string? Description, DateOnly? DueOn, bool? ClearDue, string? Status);

/// <summary>
/// The unified inbox and the commitments ledger. Audit entries carry ids only, never message text or promises.
/// </summary>
internal static class InboxEndpoints
{
    public static RouteGroupBuilder MapInboxEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var inboxGroup = api.MapGroup("/inbox");

        inboxGroup.MapGet("", async (string? states, bool? sync, IInboxService inbox, ICommitmentService commitments,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            HashSet<string>? wanted = null;
            if (!string.IsNullOrWhiteSpace(states))
            {
                wanted = states.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
                if (wanted.Any(x => !InboxStates.IsValid(x)))
                    return EndpointHelpers.Invalid("states", "Unknown state.");
            }
            if (sync == true) await inbox.SyncAsync(currentUser.OwnerId, ct);
            var listing = await inbox.ListAsync(currentUser.OwnerId, wanted, ct);
            var today = await commitments.TodayAsync(currentUser.OwnerId, ct);
            return Results.Ok(new InboxResponse(listing.Threads.Select(ToDto).ToArray(), listing.Counts,
                await commitments.SummarizeAsync(currentUser.OwnerId, today, ct)));
        }).WithName("ListInbox");

        inboxGroup.MapPost("/sync", async (IInboxService inbox, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await inbox.SyncAsync(currentUser.OwnerId, ct))).WithName("SyncInbox");

        inboxGroup.MapPut("/{id:guid}/state", async (Guid id, InboxStateRequest request, IInboxService inbox,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await inbox.SetStateAsync(id, currentUser.OwnerId,
                request.State?.Trim().ToLowerInvariant() ?? "", ct);
            return Respond(result, ToDto);
        }).WithName("SetInboxState");

        inboxGroup.MapPost("/{id:guid}/snooze", async (Guid id, InboxSnoozeRequest request, IInboxService inbox,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Until is not { } until) return EndpointHelpers.Invalid("until", "Pick a time.");
            return Respond(await inbox.SnoozeAsync(id, currentUser.OwnerId, until, ct), ToDto);
        }).WithName("SnoozeInboxThread");

        inboxGroup.MapPost("/{id:guid}/triage", async (Guid id, IInboxService inbox, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await inbox.TriageAsync(id, currentUser.OwnerId, ct);
            if (result.Succeeded)
                await AuditAsync(audit, logger, currentUser, "inbox.triaged", id, ct);
            return Respond(result, outcome => new InboxTriageDto(ToDto(outcome.Thread),
                outcome.NewCommitments.Select(ToDto).ToArray()));
        }).WithName("TriageInboxThread");

        inboxGroup.MapDelete("/{id:guid}", async (Guid id, IInboxService inbox, ICurrentUser currentUser,
            CancellationToken ct) =>
            await inbox.DeleteAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteInboxThread");

        var group = api.MapGroup("/commitments");

        group.MapGet("", async (string? direction, string? status, ICommitmentService commitments,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (direction is not null && !CommitmentDirections.IsValid(direction))
                return EndpointHelpers.Invalid("direction", "Use i_owe or owed_to_me.");
            if (status is not null && !CommitmentStatuses.IsValid(status))
                return EndpointHelpers.Invalid("status", "Use open, done, or dropped.");
            var list = await commitments.ListAsync(currentUser.OwnerId, direction, status, true, ct);
            return Results.Ok(list.Select(ToDto).ToArray());
        }).WithName("ListCommitments");

        group.MapPost("", async (CommitmentRequest request, ICommitmentService commitments, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await commitments.TodayAsync(currentUser.OwnerId, ct);
            var result = await commitments.CreateAsync(currentUser.OwnerId,
                new CommitmentDraft(request.Direction, request.Counterparty, request.Description, request.DueOn),
                today, ct);
            if (result.Succeeded)
                await AuditAsync(audit, logger, currentUser, "commitment.created", result.Value!.Id, ct);
            return result.Succeeded
                ? Results.Created($"/api/v1/commitments/{result.Value!.Id}", ToDto(result.Value))
                : Respond(result, ToDto);
        }).WithName("CreateCommitment");

        group.MapPatch("/{id:guid}", async (Guid id, CommitmentUpdateRequest request, ICommitmentService commitments,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await commitments.TodayAsync(currentUser.OwnerId, ct);
            InboxOperation<Commitment>? result = null;
            if (request.Description is not null || request.DueOn is not null || request.ClearDue == true)
            {
                result = await commitments.UpdateAsync(id, currentUser.OwnerId, request.Description, request.DueOn,
                    request.ClearDue == true, today, ct);
                if (!result.Succeeded) return Respond(result, ToDto);
            }
            if (request.Status is not null)
            {
                result = request.Status == "accepted"
                    ? await commitments.AcceptAsync(id, currentUser.OwnerId, today, ct)
                    : await commitments.SetStatusAsync(id, currentUser.OwnerId, request.Status, ct);
                if (!result.Succeeded) return Respond(result, ToDto);
            }
            if (result is null) return EndpointHelpers.Invalid("request", "Nothing to change.");
            await AuditAsync(audit, logger, currentUser, "commitment.updated", id, ct);
            return Respond(result, ToDto);
        }).WithName("UpdateCommitment");

        group.MapDelete("/{id:guid}", async (Guid id, ICommitmentService commitments, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await commitments.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "commitment.deleted", id, ct);
            return Results.NoContent();
        }).WithName("DeleteCommitment");

        return api;
    }

    private static IResult Respond<T, R>(InboxOperation<T> result, Func<T, R> map) => result.Failure switch
    {
        InboxFailure.None => Results.Ok(map(result.Value!)),
        InboxFailure.NotFound => Results.NotFound(),
        InboxFailure.Unavailable => ApiProblemResults.Validation("thread", result.Message ?? "Not available."),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    internal static InboxThreadDto ToDto(InboxThread x) => new(x.Id, x.Source, x.Title, x.Counterparty, x.State,
        x.Priority, x.Summary, x.SuggestedReply, x.LastMessagePreview, x.LastFromMe, x.LastMessageAt, x.SnoozedUntil,
        x.TriagedAt);

    internal static CommitmentDto ToDto(Commitment x) => new(x.Id, x.Direction, x.Counterparty, x.Description,
        x.DueOn, x.Status, x.Suggested, x.Source, x.InboxThreadId, x.ReminderId is not null, x.CompletedAt,
        x.CreatedAt);

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid resourceId, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "inbox", action, "low", true, null,
            JsonSerializer.Serialize(new { resourceId, source = "app" }), ct);
}
