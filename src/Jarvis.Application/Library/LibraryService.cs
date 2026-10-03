using Jarvis.Application.Automations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Library;

namespace Jarvis.Application.Library;

public sealed class LibraryService(ILibraryRepository repository, IWebPageFetcher fetcher, ILibraryDigester digester,
    IDailyBriefingRepository briefings, TimeProvider? timeProvider = null) : ILibraryService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    public Task<IReadOnlyList<LibraryItem>> ListAsync(Guid ownerId, LibraryQuery query,
        CancellationToken cancellationToken) =>
        repository.ListAsync(ownerId, query with { Limit = Math.Clamp(query.Limit, 1, LibraryRules.MaxListLimit) },
            cancellationToken);

    public Task<LibraryItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetAsync(id, ownerId, cancellationToken);

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<LibraryOperation<LibraryItem>> ClipAsync(Guid ownerId, string? url, string? note,
        Guid? projectId, string origin, CancellationToken cancellationToken)
    {
        // A link to a section ("…/post#comments") is the same page.
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed) && parsed.Fragment.Length > 0)
            url = parsed.GetLeftPart(UriPartial.Query);
        try
        {
            AutomationRuleValidator.ValidatePublicHttpsUrl(url);
        }
        catch (ArgumentException exception)
        {
            return LibraryOperation<LibraryItem>.Invalid("url", exception.Message);
        }
        var normalized = LibraryRules.NormalizeUrl(url);
        if (normalized is null) return LibraryOperation<LibraryItem>.Invalid("url", "Use an https link.");
        if (await repository.FindByUrlAsync(ownerId, normalized, cancellationToken) is { } existing)
            return LibraryOperation<LibraryItem>.Ok(existing);

        FetchedPage page;
        try
        {
            page = await fetcher.FetchAsync(normalized, cancellationToken);
        }
        catch (WebFetchException exception)
        {
            return LibraryOperation<LibraryItem>.Unavailable(exception.Message);
        }

        var isPlain = page.ContentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase);
        var extracted = isPlain
            ? new HtmlTextExtractor.Extracted(null, LibraryRules.CleanContent(page.Body))
            : HtmlTextExtractor.Extract(page.Body);
        if (extracted.Text.Length < 80)
            return LibraryOperation<LibraryItem>.Invalid("url", "Jarvis could not find readable text on that page.");

        var host = new Uri(page.FinalUrl).IdnHost;
        var title = LibraryRules.Limit(extracted.Title, LibraryRules.MaxTitleLength) ?? host;
        var digest = await digester.DigestAsync(ownerId, title, extracted.Text, cancellationToken);
        var comment = LibraryRules.Limit(note, 500);
        var now = clock.GetUtcNow();
        var item = new LibraryItem(Guid.CreateVersion7(), ownerId, LibraryKinds.Web, normalized, title,
            LibraryRules.Limit(digest.Summary, LibraryRules.MaxSummaryLength) ?? title, CleanPoints(digest.KeyPoints),
            LibraryRules.NormalizeTags(digest.Tags),
            comment is null ? extracted.Text : $"Your note: {comment}\n\n{extracted.Text}",
            LibraryOrigins.IsValid(origin) ? origin : LibraryOrigins.App, projectId, now, now);
        await repository.AddAsync(item, cancellationToken);
        await StoreCardsAsync(ownerId, item.Id, digest.Cards, await TodayAsync(ownerId, cancellationToken),
            cancellationToken);
        return LibraryOperation<LibraryItem>.Ok(item);
    }

    public async Task<LibraryOperation<LibraryItem>> AddNoteAsync(Guid ownerId, NoteDraft draft,
        CancellationToken cancellationToken)
    {
        if (draft.Kind is not (LibraryKinds.Note or LibraryKinds.Report))
            return LibraryOperation<LibraryItem>.Invalid("kind", "Use note or report.");
        var content = LibraryRules.CleanContent(draft.Content);
        if (content.Length == 0) return LibraryOperation<LibraryItem>.Invalid("content", "Write something to save.");
        var title = LibraryRules.Limit(draft.Title, LibraryRules.MaxTitleLength) ??
                    LibraryRules.Limit(content.Split('\n')[0], LibraryRules.MaxTitleLength)!;

        string? url = null;
        if (!string.IsNullOrWhiteSpace(draft.Url))
        {
            try
            {
                AutomationRuleValidator.ValidatePublicHttpsUrl(draft.Url);
                url = LibraryRules.NormalizeUrl(draft.Url);
            }
            catch (ArgumentException exception)
            {
                return LibraryOperation<LibraryItem>.Invalid("url", exception.Message);
            }
        }

        var digest = content.Length >= 400 || draft.MakeCards
            ? await digester.DigestAsync(ownerId, title, content, cancellationToken)
            : LibraryHeuristics.Digest(title, content);
        var tags = LibraryRules.NormalizeTags((draft.Tags ?? []).Concat(digest.Tags));
        var now = clock.GetUtcNow();
        var item = new LibraryItem(Guid.CreateVersion7(), ownerId, draft.Kind, url, title,
            LibraryRules.Limit(digest.Summary, LibraryRules.MaxSummaryLength) ?? title, CleanPoints(digest.KeyPoints),
            tags, content, LibraryOrigins.IsValid(draft.Origin) ? draft.Origin : LibraryOrigins.App, draft.ProjectId,
            now, now);
        await repository.AddAsync(item, cancellationToken);
        if (draft.MakeCards)
            await StoreCardsAsync(ownerId, item.Id, digest.Cards, await TodayAsync(ownerId, cancellationToken),
                cancellationToken);
        return LibraryOperation<LibraryItem>.Ok(item);
    }

    public async Task<LibraryOperation<LibraryItem>> SetTagsAsync(Guid id, Guid ownerId, IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        var item = await repository.GetAsync(id, ownerId, cancellationToken);
        if (item is null) return LibraryOperation<LibraryItem>.NotFound();
        var updated = item with { Tags = LibraryRules.NormalizeTags(tags), UpdatedAt = clock.GetUtcNow() };
        await repository.UpdateAsync(updated, cancellationToken);
        return LibraryOperation<LibraryItem>.Ok(updated);
    }

    public async Task<LibraryDigestReport> DigestAsync(Guid ownerId, int days, DateOnly today, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 90);
        var items = await repository.ListSinceAsync(ownerId, now.AddDays(-days), 30, cancellationToken);
        var tags = items.SelectMany(x => x.Tags).GroupBy(x => x).OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal).Take(5).Select(g => g.Key).ToArray();
        return new LibraryDigestReport(days, items, await CardStatsAsync(ownerId, today, cancellationToken), tags);
    }

    public Task<IReadOnlyList<Flashcard>> DueCardsAsync(Guid ownerId, DateOnly today, int limit,
        CancellationToken cancellationToken) =>
        repository.ListDueCardsAsync(ownerId, today, Math.Clamp(limit, 1, 50), cancellationToken);

    public async Task<CardStats> CardStatsAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken)
    {
        var all = await repository.ListCardsAsync(ownerId, null, cancellationToken);
        return new CardStats(all.Count, all.Count(x => x.DueOn <= today), all.Count(x => x.Repetitions == 0),
            all.Count(x => x.Repetitions >= 3));
    }

    public async Task<IReadOnlyList<Flashcard>> AddCardsAsync(Guid ownerId, Guid? itemId,
        IReadOnlyList<CardDraft> cards, DateOnly today, CancellationToken cancellationToken)
    {
        if (itemId is { } id && await repository.GetAsync(id, ownerId, cancellationToken) is null) return [];
        return await StoreCardsAsync(ownerId, itemId, cards, today, cancellationToken);
    }

    public async Task<LibraryOperation<Flashcard>> ReviewAsync(Guid cardId, Guid ownerId, int quality, DateOnly today,
        CancellationToken cancellationToken)
    {
        if (quality is < 0 or > 5) return LibraryOperation<Flashcard>.Invalid("quality", "Grade between 0 and 5.");
        var card = await repository.GetCardAsync(cardId, ownerId, cancellationToken);
        if (card is null) return LibraryOperation<Flashcard>.NotFound();
        var next = Sm2.Review(card, quality, today);
        await repository.UpdateCardAsync(next, cancellationToken);
        return LibraryOperation<Flashcard>.Ok(next);
    }

    public Task<bool> DeleteCardAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteCardAsync(id, ownerId, cancellationToken);

    private async Task<IReadOnlyList<Flashcard>> StoreCardsAsync(Guid ownerId, Guid? itemId,
        IReadOnlyList<CardDraft> drafts, DateOnly today, CancellationToken cancellationToken)
    {
        var known = (await repository.ListCardsAsync(ownerId, itemId, cancellationToken))
            .Select(x => x.Front.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        var cards = new List<Flashcard>();
        foreach (var draft in drafts)
        {
            var front = LibraryRules.Limit(draft.Front, LibraryRules.MaxCardTextLength);
            var back = LibraryRules.Limit(draft.Back, LibraryRules.MaxCardTextLength);
            if (front is null || back is null || !known.Add(front.ToLowerInvariant())) continue;
            cards.Add(new Flashcard(Guid.CreateVersion7(), ownerId, itemId, front, back, Sm2.StartEase, 0, 0, 0,
                today, null, now));
            if (cards.Count == LibraryRules.MaxCardsPerItem) break;
        }
        if (cards.Count > 0) await repository.AddCardsAsync(cards, cancellationToken);
        return cards;
    }

    private static IReadOnlyList<string> CleanPoints(IReadOnlyList<string> points) =>
        points.Select(x => LibraryRules.Limit(x, LibraryRules.MaxKeyPointLength)).Where(x => x is not null)
            .Select(x => x!).Take(LibraryRules.MaxKeyPoints).ToArray();
}

public sealed class ResearchService(IJarvisTaskService tasks) : IResearchService
{
    public const int MinQuestionLength = 8;
    public const int MaxQuestionLength = 1_000;

    public async Task<LibraryOperation<ResearchStarted>> StartAsync(Guid ownerId, string? question, string? depth,
        Guid? projectId, CancellationToken cancellationToken)
    {
        var clean = LibraryRules.Clean(question);
        if (clean is null || clean.Length < MinQuestionLength)
            return LibraryOperation<ResearchStarted>.Invalid("question", "Ask a complete question to research.");
        if (clean.Length > MaxQuestionLength)
            return LibraryOperation<ResearchStarted>.Invalid("question",
                $"Keep the question under {MaxQuestionLength} characters.");
        var chosen = depth?.Trim().ToLowerInvariant() ?? "standard";
        if (!ResearchPrompt.Depths.Contains(chosen))
            return LibraryOperation<ResearchStarted>.Invalid("depth", "Use quick, standard, or deep.");

        try
        {
            var title = "Research: " + (clean.Length <= 80 ? clean : clean[..79] + "…");
            var task = await tasks.CreateAsync(ownerId, title, ResearchPrompt.Build(clean, chosen), cancellationToken,
                projectId: projectId);
            return LibraryOperation<ResearchStarted>.Ok(new ResearchStarted(new JarvisTaskRecordLite(task.Id, task.Title)));
        }
        catch (ArgumentException exception)
        {
            return LibraryOperation<ResearchStarted>.Invalid("projectId", exception.Message);
        }
    }
}
