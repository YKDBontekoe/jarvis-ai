using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Library;
using Jarvis.Domain.Library;

namespace Jarvis.Api.Endpoints;

public sealed record ClipRequest(string? Url, string? Note);

public sealed record NoteRequest(string? Title, string? Content, string? Kind, string? Url, string[]? Tags,
    bool? MakeFlashcards, Guid? ProjectId = null);

public sealed record TagsRequest(string[]? Tags);

public sealed record ResearchRequest(string? Question, string? Depth, Guid? ProjectId);

public sealed record ReviewRequest(int? Quality, string? Button);

public sealed record CardRequest(string? Front, string? Back, Guid? ItemId);

public sealed record LibraryItemDto(Guid Id, string Kind, string? Url, string Title, string Summary,
    IReadOnlyList<string> KeyPoints, IReadOnlyList<string> Tags, string? Content, string Origin,
    DateTimeOffset CreatedAt);

public sealed record FlashcardDto(Guid Id, Guid? ItemId, string Front, string Back, int IntervalDays, int Repetitions,
    DateOnly DueOn);

public sealed record LibraryOverviewDto(IReadOnlyList<LibraryItemDto> Items, CardStats Cards);

public sealed record LibraryDigestDto(int Days, IReadOnlyList<LibraryItemDto> Items, CardStats Cards,
    IReadOnlyList<string> TopTags);

/// <summary>
/// The owner's library: clipped pages, notes and research reports, flashcards, and deep research tasks. Audit
/// entries carry ids only, never titles or page text.
/// </summary>
internal static class LibraryEndpoints
{
    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/library");

        group.MapGet("", async (string? q, string? tag, string? kind, int? limit, ILibraryService library,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (kind is not null && !LibraryKinds.IsValid(kind)) return EndpointHelpers.Invalid("kind", "Unknown kind.");
            var items = await library.ListAsync(currentUser.OwnerId,
                new LibraryQuery(q, tag, kind, limit ?? LibraryRules.DefaultListLimit), ct);
            var today = await library.TodayAsync(currentUser.OwnerId, ct);
            return Results.Ok(new LibraryOverviewDto(items.Select(x => ToDto(x, false)).ToArray(),
                await library.CardStatsAsync(currentUser.OwnerId, today, ct)));
        }).WithName("ListLibrary");

        group.MapGet("/{id:guid}", async (Guid id, ILibraryService library, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var item = await library.GetAsync(id, currentUser.OwnerId, ct);
            return item is null ? Results.NotFound() : Results.Ok(ToDto(item, true));
        }).WithName("GetLibraryItem");

        group.MapPost("/clip", async (ClipRequest request, ILibraryService library, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await library.ClipAsync(currentUser.OwnerId, request.Url, request.Note, null,
                LibraryOrigins.App, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "library.clipped", result.Value!.Id, ct);
            return Respond(result, item => ToDto(item, false));
        }).WithName("ClipToLibrary");

        group.MapPost("/notes", async (NoteRequest request, ILibraryService library, IAuditEventStore audit,
            Jarvis.Application.Projects.IProjectStore projects, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.ProjectId is { } projectId &&
                await projects.GetAsync(projectId, currentUser.OwnerId, ct) is null)
                return EndpointHelpers.Invalid("projectId", "That project was not found.");
            var result = await library.AddNoteAsync(currentUser.OwnerId, new NoteDraft(request.Title, request.Content,
                request.Kind?.Trim().ToLowerInvariant() ?? LibraryKinds.Note, request.Url, request.Tags,
                LibraryOrigins.App, request.ProjectId, request.MakeFlashcards == true), ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "library.noted", result.Value!.Id, ct);
            return Respond(result, item => ToDto(item, false));
        }).WithName("AddLibraryNote");

        group.MapPut("/{id:guid}/tags", async (Guid id, TagsRequest request, ILibraryService library,
            ICurrentUser currentUser, CancellationToken ct) =>
            Respond(await library.SetTagsAsync(id, currentUser.OwnerId, request.Tags ?? [], ct),
                item => ToDto(item, false))).WithName("SetLibraryTags");

        group.MapDelete("/{id:guid}", async (Guid id, ILibraryService library, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await library.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "library.deleted", id, ct);
            return Results.NoContent();
        }).WithName("DeleteLibraryItem");

        group.MapGet("/digest", async (int? days, ILibraryService library, TimeProvider clock,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await library.TodayAsync(currentUser.OwnerId, ct);
            var report = await library.DigestAsync(currentUser.OwnerId, days ?? 7, today, clock.GetUtcNow(), ct);
            return Results.Ok(new LibraryDigestDto(report.Days, report.Items.Select(x => ToDto(x, false)).ToArray(),
                report.Cards, report.TopTags));
        }).WithName("LibraryDigest");

        group.MapPost("/research", async (ResearchRequest request, IResearchService research, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await research.StartAsync(currentUser.OwnerId, request.Question, request.Depth,
                request.ProjectId, ct);
            if (result.Succeeded) await AuditAsync(audit, logger, currentUser, "research.started", result.Value!.Task.Id, ct);
            return result.Succeeded
                ? Results.Accepted($"/api/v1/tasks/{result.Value!.Task.Id}", new { taskId = result.Value.Task.Id, title = result.Value.Task.Title })
                : Respond(result, x => x);
        }).WithName("StartDeepResearch");

        // A short lesson built only from the owner's saved text: quoted points, questions and an answer key that
        // always comes from the same source. Nothing is stored until the owner picks cards to keep.
        group.MapGet("/{id:guid}/lesson", async (Guid id, ILibraryService library, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var item = await library.GetAsync(id, currentUser.OwnerId, ct);
            if (item is null) return Results.NotFound();
            return LessonBuilder.Build(item) is { } lesson
                ? Results.Ok(lesson)
                : EndpointHelpers.Invalid("item", "There is not enough text in this item to teach from.");
        }).WithName("GetLesson");

        group.MapPost("/{id:guid}/lesson/check", async (Guid id, LessonCheckRequest request, ILibraryService library,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var item = await library.GetAsync(id, currentUser.OwnerId, ct);
            if (item is null) return Results.NotFound();
            return LessonBuilder.Check(item, request.QuestionId, request.Answer) is { } grade
                ? Results.Ok(grade)
                : EndpointHelpers.Invalid("questionId", "That question is not part of this lesson.");
        }).WithName("CheckLessonAnswer");

        group.MapPost("/{id:guid}/lesson/cards", async (Guid id, LessonCardsRequest request, ILibraryService library,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var item = await library.GetAsync(id, currentUser.OwnerId, ct);
            if (item is null) return Results.NotFound();
            var drafts = LessonBuilder.Cards(item, request.QuestionIds ?? []);
            if (drafts.Count == 0)
                return EndpointHelpers.Invalid("questionIds", "Pick at least one fill-in question to keep as a card.");
            var today = await library.TodayAsync(currentUser.OwnerId, ct);
            var added = await library.AddCardsAsync(currentUser.OwnerId, id, drafts, today, ct);
            return Results.Ok(new { added = added.Count, skipped = drafts.Count - added.Count });
        }).WithName("KeepLessonCards");

        var cards = group.MapGroup("/cards");

        cards.MapGet("/due", async (int? limit, ILibraryService library, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await library.TodayAsync(currentUser.OwnerId, ct);
            var due = await library.DueCardsAsync(currentUser.OwnerId, today, limit ?? 20, ct);
            return Results.Ok(new { cards = due.Select(ToDto).ToArray(), stats = await library.CardStatsAsync(currentUser.OwnerId, today, ct) });
        }).WithName("DueFlashcards");

        cards.MapPost("", async (CardRequest request, ILibraryService library, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (LibraryRules.Clean(request.Front) is null || LibraryRules.Clean(request.Back) is null)
                return EndpointHelpers.Invalid("front", "Write both a question and an answer.");
            var today = await library.TodayAsync(currentUser.OwnerId, ct);
            var added = await library.AddCardsAsync(currentUser.OwnerId, request.ItemId,
                [new CardDraft(request.Front!, request.Back!)], today, ct);
            return added.Count == 0
                ? EndpointHelpers.Invalid("front", "That card already exists, or its item was not found.")
                : Results.Created($"/api/v1/library/cards/{added[0].Id}", ToDto(added[0]));
        }).WithName("AddFlashcard");

        cards.MapPost("/{id:guid}/review", async (Guid id, ReviewRequest request, ILibraryService library,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var quality = request.Quality ?? (request.Button is null ? -1 : Sm2.QualityFor(request.Button));
            var today = await library.TodayAsync(currentUser.OwnerId, ct);
            return Respond(await library.ReviewAsync(id, currentUser.OwnerId, quality, today, ct), ToDto);
        }).WithName("ReviewFlashcard");

        cards.MapDelete("/{id:guid}", async (Guid id, ILibraryService library, ICurrentUser currentUser,
            CancellationToken ct) =>
            await library.DeleteCardAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteFlashcard");

        return api;
    }

    private static IResult Respond<T, R>(LibraryOperation<T> result, Func<T, R> map) => result.Failure switch
    {
        LibraryFailure.None => Results.Ok(map(result.Value!)),
        LibraryFailure.NotFound => Results.NotFound(),
        LibraryFailure.Unavailable => ApiProblemResults.Validation("url", result.Message ?? "Not available."),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    internal static LibraryItemDto ToDto(LibraryItem x, bool withContent) => new(x.Id, x.Kind, x.Url, x.Title,
        x.Summary, x.KeyPoints, x.Tags, withContent ? x.Content : null, x.Origin, x.CreatedAt);

    internal static FlashcardDto ToDto(Flashcard x) =>
        new(x.Id, x.ItemId, x.Front, x.Back, x.IntervalDays, x.Repetitions, x.DueOn);

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid resourceId, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "library", action, "low", true, null,
            JsonSerializer.Serialize(new { resourceId, source = "app" }), ct);
}
