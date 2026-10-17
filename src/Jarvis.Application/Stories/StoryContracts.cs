using System.Text;
using Jarvis.Application.Timeline;

namespace Jarvis.Application.Stories;

public static class StoryStyles
{
    public const string Recap = "recap";
    public const string Retrospective = "retrospective";
    public const string WhatChanged = "what_changed";

    public static readonly string[] All = [Recap, Retrospective, WhatChanged];
}

public static class StoryRules
{
    public const int MaxDrafts = 20;
    public const int MaxItems = 30;
    public const int MaxWindowDays = 120;
    public const int MaxText = 300;
    public const int MinPatternDays = 14;

    /// <summary>Included unless the owner chooses otherwise; money, contacts and chats are opt-in.</summary>
    public static readonly string[] DefaultKinds =
        [TimelineKinds.Decision, TimelineKinds.Task, TimelineKinds.Journal];

    public static readonly string[] SensitiveKinds =
        [TimelineKinds.Expense, TimelineKinds.Contact, TimelineKinds.Birthday, TimelineKinds.Conversation];
}

/// <summary>
/// One moment in a story. <see cref="Text"/> is the owner's wording (editable); <see cref="OriginalText"/> is what the
/// record said, and the original record is never changed by editing a story.
/// </summary>
public sealed record StoryItem(
    string Id,
    string Kind,
    DateOnly Date,
    string Text,
    string OriginalText,
    string SourceRef,
    bool Removed = false);

public sealed record StoryPattern(string Headline, string Detail, string Limits);

public sealed record StoryDraft(
    Guid Id,
    string Title,
    string Style,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<StoryItem> Items,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<StoryPattern> Patterns,
    string? PatternsNote,
    string? Reflection,
    int Version,
    int SavedVersion,
    Guid? LibraryItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Owner settings section <see cref="Section"/>: story drafts, newest first.</summary>
public sealed record StoryState(IReadOnlyList<StoryDraft> Drafts)
{
    public const string Section = "stories.drafts";
}

public sealed record StoryRequest(DateOnly? From, DateOnly? To, string? Style, IReadOnlyList<string>? Kinds);

public sealed record StoryEditRequest(string? Text);

public sealed record StoryMoveRequest(string? Direction);

public sealed record StoryRemoveRequest(bool? Removed);

public sealed record StoryExport(string FileName, string ContentType, string Content, int Left);

public enum StoryFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

public sealed record StoryOperation<T>(T? Value, StoryFailure Failure = StoryFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == StoryFailure.None;
    public static StoryOperation<T> Ok(T value) => new(value);
    public static StoryOperation<T> NotFound() => new(default, StoryFailure.NotFound);
    public static StoryOperation<T> Invalid(string field, string message) =>
        new(default, StoryFailure.Invalid, field, message);
    public static StoryOperation<T> Conflict(string field, string message) =>
        new(default, StoryFailure.Conflict, field, message);
}

/// <summary>
/// Turns timeline moments into the outline of a story. The counts are of real moments in the window; every item
/// points at its record; patterns carry their window and limits and are left out when there is too little data.
/// </summary>
public static class StoryBuilder
{
    private static readonly Dictionary<string, int> Priority = new()
    {
        [TimelineKinds.Decision] = 0, [TimelineKinds.Task] = 1, [TimelineKinds.Journal] = 2
    };

    public static (IReadOnlyList<StoryItem> Items, IReadOnlyDictionary<string, int> Counts) Outline(
        IEnumerable<TimelineEvent> events)
    {
        var list = events.ToArray();
        var counts = list.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        var items = list
            .OrderBy(e => Priority.GetValueOrDefault(e.Kind, 3)).ThenBy(e => e.At)
            .Take(StoryRules.MaxItems)
            .OrderBy(e => e.Date).ThenBy(e => e.At)
            .Select((e, index) =>
            {
                var text = Describe(e);
                return new StoryItem($"i{index + 1}", e.Kind, e.Date, text, text,
                    e.TargetId is { } id ? $"{e.Kind}:{id}" : $"{e.Kind}:{e.Id}");
            }).ToArray();
        return (items, counts);
    }

    private static string Describe(TimelineEvent e) =>
        e.Detail is { Length: > 0 } detail && e.Kind == TimelineKinds.Journal
            ? $"{e.Title}: {TimelineRules.Shorten(detail, 140)}"
            : e.Title;

    /// <summary>One next step, not a to-do list: the first decision still waiting, else a single reflection question.</summary>
    public static string Reflection(IReadOnlyList<StoryItem> items, string style)
    {
        var decision = items.FirstOrDefault(i => i.Kind == TimelineKinds.Decision && !i.Removed);
        if (decision is not null) return $"Is “{decision.Text}” still the right call? Revisit it when you have a minute.";
        return style == StoryStyles.WhatChanged
            ? "Looking at what changed, what is one thing you want to keep doing?"
            : "Which of these moments mattered most to you? A line in your journal would keep it.";
    }

    public static string ToMarkdown(StoryDraft draft, IReadOnlySet<string>? redact, out int left)
    {
        var hidden = redact ?? new HashSet<string>();
        var shown = draft.Items.Where(i => !i.Removed && !hidden.Contains(i.Kind)).ToArray();
        left = draft.Items.Count(i => !i.Removed) - shown.Length;
        var text = new StringBuilder();
        text.Append("# ").AppendLine(draft.Title);
        text.AppendLine().AppendLine($"_{draft.From:d MMM yyyy} to {draft.To:d MMM yyyy}. Moments are taken from records; the wording may be edited._");
        text.AppendLine().AppendLine("## What happened");
        foreach (var group in shown.GroupBy(i => i.Date))
            foreach (var item in group)
                text.Append("- ").Append(item.Date.ToString("d MMM")).Append(": ").Append(item.Text).AppendLine();
        var counts = draft.Counts.Where(pair => !hidden.Contains(pair.Key)).OrderBy(pair => pair.Key).ToArray();
        if (counts.Length > 0)
        {
            text.AppendLine().AppendLine("## In numbers");
            foreach (var (kind, count) in counts) text.AppendLine($"- {count} {kind}");
        }
        if (hidden.Count == 0 && draft.Patterns.Count > 0)
        {
            text.AppendLine().AppendLine("## Patterns (observations, not explanations)");
            foreach (var pattern in draft.Patterns)
                text.Append("- ").Append(pattern.Headline).Append(": ").Append(pattern.Detail).Append(' ')
                    .AppendLine(pattern.Limits);
        }
        if (!string.IsNullOrWhiteSpace(draft.Reflection))
            text.AppendLine().AppendLine("## One thing to think about").AppendLine(draft.Reflection);
        if (hidden.Count > 0)
            text.AppendLine().AppendLine($"_A shareable version: {string.Join(", ", hidden.OrderBy(k => k))} left out._");
        return text.ToString().TrimEnd();
    }
}

public interface IStoryService
{
    Task<IReadOnlyList<StoryDraft>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<StoryDraft?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<StoryOperation<StoryDraft>> CreateAsync(Guid ownerId, StoryRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<StoryOperation<StoryDraft>> EditItemAsync(Guid ownerId, Guid id, string itemId, string? text,
        CancellationToken cancellationToken);
    Task<StoryOperation<StoryDraft>> SetRemovedAsync(Guid ownerId, Guid id, string itemId, bool removed,
        CancellationToken cancellationToken);
    Task<StoryOperation<StoryDraft>> MoveItemAsync(Guid ownerId, Guid id, string itemId, string? direction,
        CancellationToken cancellationToken);

    /// <summary>Saves a private Library report (all kept moments). A new version is a new item.</summary>
    Task<StoryOperation<StoryDraft>> SaveAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>A file with only the chosen content; <paramref name="redact"/> lists kinds (or <c>people</c>) to leave out.</summary>
    Task<StoryOperation<StoryExport>> ExportAsync(Guid ownerId, Guid id, string? redact,
        CancellationToken cancellationToken);
}
