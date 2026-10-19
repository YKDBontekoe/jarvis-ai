using Jarvis.Application.Library;
using Jarvis.Application.Settings;
using Jarvis.Application.Timeline;

namespace Jarvis.Application.Stories;

/// <summary>
/// Story drafts over the factual timeline, kept in owner settings. Editing a story never changes the records it
/// came from; saving writes a private Library report; exporting returns a file with only the chosen content.
/// </summary>
public sealed class StoryService(
    IOwnerSettingsStore settings,
    ITimelineService timeline,
    ILibraryService library,
    TimeProvider? timeProvider = null) : IStoryService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<StoryDraft>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).OrderByDescending(d => d.UpdatedAt).ToArray();

    public async Task<StoryDraft?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).FirstOrDefault(draft => draft.Id == id);

    public async Task<StoryOperation<StoryDraft>> CreateAsync(Guid ownerId, StoryRequest request,
        CancellationToken cancellationToken)
    {
        var today = await timeline.TodayAsync(ownerId, cancellationToken);
        var to = request.To ?? today;
        var from = request.From ?? to.AddDays(-6);
        if (from > to) return StoryOperation<StoryDraft>.Invalid("from", "The start must not be after the end.");
        if (to > today) return StoryOperation<StoryDraft>.Invalid("to", "A story cannot reach into the future.");
        if (to.DayNumber - from.DayNumber + 1 > StoryRules.MaxWindowDays)
            return StoryOperation<StoryDraft>.Invalid("from", $"Choose at most {StoryRules.MaxWindowDays} days.");
        var style = request.Style ?? StoryStyles.Recap;
        if (!StoryStyles.All.Contains(style))
            return StoryOperation<StoryDraft>.Invalid("style", "Choose recap, retrospective or what_changed.");
        var kinds = (request.Kinds is { Count: > 0 } ? request.Kinds : StoryRules.DefaultKinds).Distinct().ToArray();
        if (kinds.Any(kind => !TimelineKinds.IsValid(kind)))
            return StoryOperation<StoryDraft>.Invalid("kinds", "One of those kinds is not on the timeline.");
        var all = await LoadAsync(ownerId, cancellationToken);
        if (all.Count >= StoryRules.MaxDrafts)
            return StoryOperation<StoryDraft>.Conflict("drafts", $"Keep at most {StoryRules.MaxDrafts} stories. Delete one first.");

        var result = await timeline.QueryAsync(ownerId,
            new TimelineQuery(from, to, new HashSet<string>(kinds), null, TimelineRules.MaxLimit), cancellationToken);
        var (items, counts) = StoryBuilder.Outline(result.Days.SelectMany(day => day.Events));

        IReadOnlyList<StoryPattern> patterns = [];
        string? patternsNote = null;
        var days = to.DayNumber - from.DayNumber + 1;
        var insights = await timeline.InsightsAsync(ownerId, to, Math.Min(days, TimelineRules.MaxInsightDays), cancellationToken);
        if (insights.DaysWithData < StoryRules.MinPatternDays)
            patternsNote = $"Only {insights.DaysWithData} day(s) have enough recorded to look for patterns; that is too few to say anything.";
        else
            patterns = insights.Insights.Take(3).Select(insight => new StoryPattern(insight.Headline, insight.Detail,
                $"Based on {insights.DaysWithData} days with data. This shows what moved together, not why.")).ToArray();

        var now = clock.GetUtcNow();
        var draft = new StoryDraft(Guid.NewGuid(), $"{from:d MMM} to {to:d MMM yyyy}", style, from, to, kinds, items,
            counts, patterns, patternsNote, StoryBuilder.Reflection(items, style), 1, 0, null, now, now);
        await SaveStateAsync(ownerId, [draft, .. all], cancellationToken);
        return StoryOperation<StoryDraft>.Ok(draft);
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        if (all.All(draft => draft.Id != id)) return false;
        await SaveStateAsync(ownerId, all.Where(draft => draft.Id != id).ToArray(), cancellationToken);
        return true;
    }

    public Task<StoryOperation<StoryDraft>> EditItemAsync(Guid ownerId, Guid id, string itemId, string? text,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, draft =>
        {
            var clean = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (clean.Length == 0) return StoryOperation<IReadOnlyList<StoryItem>>.Invalid("text", "Write what this moment should say.");
            if (clean.Length > StoryRules.MaxText)
                return StoryOperation<IReadOnlyList<StoryItem>>.Invalid("text", $"Keep it under {StoryRules.MaxText} characters.");
            return Replace(draft, itemId, item => item with { Text = clean });
        }, cancellationToken);

    public Task<StoryOperation<StoryDraft>> SetRemovedAsync(Guid ownerId, Guid id, string itemId, bool removed,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, draft => Replace(draft, itemId, item => item with { Removed = removed }),
            cancellationToken);

    public Task<StoryOperation<StoryDraft>> MoveItemAsync(Guid ownerId, Guid id, string itemId, string? direction,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, draft =>
        {
            if (direction is not ("up" or "down"))
                return StoryOperation<IReadOnlyList<StoryItem>>.Invalid("direction", "Choose up or down.");
            var items = draft.Items.ToList();
            var index = items.FindIndex(item => item.Id == itemId);
            if (index < 0) return StoryOperation<IReadOnlyList<StoryItem>>.NotFound();
            var target = direction == "up" ? index - 1 : index + 1;
            if (target < 0 || target >= items.Count)
                return StoryOperation<IReadOnlyList<StoryItem>>.Conflict("direction", "It is already at the end.");
            (items[index], items[target]) = (items[target], items[index]);
            return StoryOperation<IReadOnlyList<StoryItem>>.Ok(items);
        }, cancellationToken);

    public async Task<StoryOperation<StoryDraft>> SaveAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        var draft = all.FirstOrDefault(item => item.Id == id);
        if (draft is null) return StoryOperation<StoryDraft>.NotFound();
        if (draft.SavedVersion == draft.Version && draft.LibraryItemId is not null)
            return StoryOperation<StoryDraft>.Ok(draft);
        var saved = await library.AddNoteAsync(ownerId,
            new NoteDraft($"Story: {draft.Title} (v{draft.Version})", StoryBuilder.ToMarkdown(draft, null, out _),
                LibraryKinds.Report, null, ["story", "private"], LibraryOrigins.App, null),
            cancellationToken);
        if (!saved.Succeeded)
            return StoryOperation<StoryDraft>.Invalid(saved.Field ?? "library", saved.Message ?? "Could not save to the Library.");
        var updated = draft with { SavedVersion = draft.Version, LibraryItemId = saved.Value!.Id };
        await SaveStateAsync(ownerId, all.Select(item => item.Id == id ? updated : item).ToArray(), cancellationToken);
        return StoryOperation<StoryDraft>.Ok(updated);
    }

    public async Task<StoryOperation<StoryExport>> ExportAsync(Guid ownerId, Guid id, string? redact,
        CancellationToken cancellationToken)
    {
        var draft = await GetAsync(ownerId, id, cancellationToken);
        if (draft is null) return StoryOperation<StoryExport>.NotFound();
        var hidden = new HashSet<string>();
        foreach (var part in (redact ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "people") { hidden.Add(TimelineKinds.Contact); hidden.Add(TimelineKinds.Birthday); }
            else if (TimelineKinds.IsValid(part)) hidden.Add(part);
            else return StoryOperation<StoryExport>.Invalid("redact", $"“{part}” is not a kind that can be left out.");
        }
        var markdown = StoryBuilder.ToMarkdown(draft, hidden, out var left);
        return StoryOperation<StoryExport>.Ok(new StoryExport(
            $"story-{draft.From:yyyyMMdd}-{draft.To:yyyyMMdd}-v{draft.Version}.md", "text/markdown; charset=utf-8",
            markdown, left));
    }

    private static StoryOperation<IReadOnlyList<StoryItem>> Replace(StoryDraft draft, string itemId,
        Func<StoryItem, StoryItem> change)
    {
        if (draft.Items.All(item => item.Id != itemId)) return StoryOperation<IReadOnlyList<StoryItem>>.NotFound();
        return StoryOperation<IReadOnlyList<StoryItem>>.Ok(
            draft.Items.Select(item => item.Id == itemId ? change(item) : item).ToArray());
    }

    private async Task<StoryOperation<StoryDraft>> MutateAsync(Guid ownerId, Guid id,
        Func<StoryDraft, StoryOperation<IReadOnlyList<StoryItem>>> change, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        var draft = all.FirstOrDefault(item => item.Id == id);
        if (draft is null) return StoryOperation<StoryDraft>.NotFound();
        var result = change(draft);
        if (!result.Succeeded)
            return new StoryOperation<StoryDraft>(default, result.Failure, result.Field, result.Message);
        var updated = draft with
        {
            Items = result.Value!,
            Reflection = StoryBuilder.Reflection(result.Value!, draft.Style),
            Version = draft.Version + 1,
            UpdatedAt = clock.GetUtcNow()
        };
        await SaveStateAsync(ownerId, all.Select(item => item.Id == id ? updated : item).ToArray(), cancellationToken);
        return StoryOperation<StoryDraft>.Ok(updated);
    }

    private async Task<IReadOnlyList<StoryDraft>> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await settings.GetAsync<StoryState>(ownerId, StoryState.Section, cancellationToken))?.Drafts ?? [];

    private Task SaveStateAsync(Guid ownerId, IReadOnlyList<StoryDraft> drafts, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, StoryState.Section, new StoryState(drafts), cancellationToken);
}
