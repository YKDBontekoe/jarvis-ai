using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Journal;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Endpoints;

internal static class JournalEndpoints
{
    public static RouteGroupBuilder MapJournalEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var journal = api.MapGroup("/journal");

        journal.MapGet("", async (IJournalService service, ICurrentUser currentUser, DateOnly? from, DateOnly? to,
                int? limit, CancellationToken ct) =>
            {
                if (from is not null && to is not null && from > to)
                    return EndpointHelpers.Invalid("from", "The start date must not be after the end date.");
                var entries = await service.ListAsync(currentUser.OwnerId, from, to, limit ?? 50, ct);
                return Results.Ok(entries.Select(entry => entry.ToDto()));
            })
            .WithName("ListJournalEntries");

        journal.MapGet("/summary", async (IJournalService service, IDailyBriefingRepository briefings,
                ICurrentUser currentUser, int? days, CancellationToken ct) =>
            {
                var today = await TodayAsync(briefings, currentUser, ct);
                return Results.Ok(await service.SummarizeAsync(currentUser.OwnerId, days ?? 30, today, ct));
            })
            .WithName("GetJournalSummary");

        journal.MapGet("/{id:guid}", async (Guid id, IJournalService service, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var entry = await service.GetAsync(id, currentUser.OwnerId, ct);
            return entry is null ? Results.NotFound() : Results.Ok(entry.ToDto());
        }).WithName("GetJournalEntry");

        journal.MapPost("", async (JournalRequest request, IJournalService service, IDailyBriefingRepository briefings,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await TodayAsync(briefings, currentUser, ct);
            var draft = ToDraft(request, today);
            var errors = JournalRules.Validate(draft, today);
            if (errors.Count > 0) return ApiProblemResults.Validation(errors);
            var entry = await service.CreateAsync(currentUser.OwnerId, draft, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "journal", "journal.created",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = entry.Id, source = entry.Source }), ct);
            return Results.Created($"/api/v1/journal/{entry.Id}", entry.ToDto());
        }).WithName("CreateJournalEntry");

        journal.MapPut("/{id:guid}", async (Guid id, JournalRequest request, IJournalService service,
            IDailyBriefingRepository briefings, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var existing = await service.GetAsync(id, currentUser.OwnerId, ct);
            if (existing is null) return Results.NotFound();
            var today = await TodayAsync(briefings, currentUser, ct);
            // Editing keeps how the entry was created unless the client says otherwise.
            var draft = ToDraft(request with { Source = request.Source ?? existing.Source }, today);
            var errors = JournalRules.Validate(draft, today);
            if (errors.Count > 0) return ApiProblemResults.Validation(errors);
            var entry = await service.UpdateAsync(id, currentUser.OwnerId, draft, ct);
            if (entry is null) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "journal", "journal.updated",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = id }), ct);
            return Results.Ok(entry.ToDto());
        }).WithName("UpdateJournalEntry");

        journal.MapDelete("/{id:guid}", async (Guid id, IJournalService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await service.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "journal", "journal.deleted",
                "high", true, null, JsonSerializer.Serialize(new { resourceId = id }), ct);
            return Results.NoContent();
        }).WithName("DeleteJournalEntry");

        return api;
    }

    private static JournalDraft ToDraft(JournalRequest request, DateOnly today) => new(
        request.EntryDate ?? today, request.Content, request.Highlights, request.Gratitude, request.Rating,
        request.Mood, request.Energy, request.Stress, request.Tags, request.Source ?? JournalSources.Written);

    /// <summary>"Today" in the owner's configured time zone, falling back to UTC.</summary>
    private static async Task<DateOnly> TodayAsync(IDailyBriefingRepository briefings, ICurrentUser currentUser,
        CancellationToken ct)
    {
        var zoneId = (await briefings.GetAsync(currentUser.OwnerId, ct))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), zone).DateTime);
    }
}
