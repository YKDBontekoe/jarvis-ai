using System.Globalization;
using System.Text;
using Jarvis.Domain.Journal;

namespace Jarvis.Application.Journal;

public static class JournalSources
{
    /// <summary>Typed into the journal form.</summary>
    public const string Written = "written";

    /// <summary>Told to Jarvis in a voice session.</summary>
    public const string Voice = "voice";

    /// <summary>Told to Jarvis in a text chat.</summary>
    public const string Chat = "chat";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Written, Voice, Chat };

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? source) =>
        source is not null && All.Contains(source);
}

/// <summary>User-supplied journal fields before validation.</summary>
public sealed record JournalDraft(
    DateOnly EntryDate,
    string? Content,
    string? Highlights,
    string? Gratitude,
    int? Rating,
    int? Mood,
    int? Energy,
    int? Stress,
    IReadOnlyList<string>? Tags,
    string Source = JournalSources.Written);

public sealed record JournalDaySummary(DateOnly Date, double? Rating, double? Mood, double? Energy, double? Stress,
    int Entries);

public sealed record JournalSummary(
    int Days,
    int TotalEntries,
    int CurrentStreak,
    double? AverageRating,
    double? AverageMood,
    double? AverageEnergy,
    double? AverageStress,
    IReadOnlyList<JournalDaySummary> Series);

public interface IJournalRepository
{
    Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken cancellationToken);
    Task<JournalEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<JournalEntry?> UpdateAsync(JournalEntry entry, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task SetMemoryIdAsync(Guid id, Guid ownerId, Guid? memoryId, CancellationToken cancellationToken);

    /// <summary>Newest entry date first, then newest created; <paramref name="from"/>/<paramref name="to"/> are inclusive.</summary>
    Task<IReadOnlyList<JournalEntry>> ListAsync(Guid ownerId, DateOnly? from, DateOnly? to, int limit,
        CancellationToken cancellationToken);
}

public interface IJournalService
{
    Task<JournalEntry> CreateAsync(Guid ownerId, JournalDraft draft, CancellationToken cancellationToken);

    /// <summary>
    /// Adds to the newest entry for the draft's date when one exists (text is appended, ratings that are
    /// supplied replace earlier ones), otherwise creates a new entry. Used when Jarvis journals during a chat.
    /// </summary>
    Task<(JournalEntry Entry, bool Merged)> MergeAsync(Guid ownerId, JournalDraft draft,
        CancellationToken cancellationToken);

    Task<JournalEntry?> UpdateAsync(Guid id, Guid ownerId, JournalDraft draft, CancellationToken cancellationToken);
    Task<JournalEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JournalEntry>> ListAsync(Guid ownerId, DateOnly? from, DateOnly? to, int limit,
        CancellationToken cancellationToken);
    Task<JournalSummary> SummarizeAsync(Guid ownerId, int days, DateOnly today, CancellationToken cancellationToken);
}

public static class JournalRules
{
    public const int MaxContentLength = 6_000;
    public const int MaxSectionLength = 1_000;
    public const int MaxTags = 10;
    public const int MaxTagLength = 32;
    public const int MaxListLimit = 200;

    /// <summary>Returns field errors for <paramref name="draft"/>, or an empty dictionary when it is valid.</summary>
    public static Dictionary<string, string[]> Validate(JournalDraft draft, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (!JournalSources.IsValid(draft.Source)) errors["source"] = ["Unknown journal source."];
        if (draft.EntryDate > today.AddDays(1)) errors["entryDate"] = ["Entries cannot be dated in the future."];
        if (draft.EntryDate < new DateOnly(1970, 1, 1)) errors["entryDate"] = ["Entry date is too old."];
        if ((draft.Content?.Length ?? 0) > MaxContentLength)
            errors["content"] = [$"Entry text can contain at most {MaxContentLength:N0} characters."];
        if ((draft.Highlights?.Length ?? 0) > MaxSectionLength)
            errors["highlights"] = [$"Highlights can contain at most {MaxSectionLength:N0} characters."];
        if ((draft.Gratitude?.Length ?? 0) > MaxSectionLength)
            errors["gratitude"] = [$"Gratitude can contain at most {MaxSectionLength:N0} characters."];
        if (draft.Rating is < 1 or > 10) errors["rating"] = ["Rating must be between 1 and 10."];
        if (draft.Mood is < 1 or > 5) errors["mood"] = ["Mood must be between 1 and 5."];
        if (draft.Energy is < 1 or > 5) errors["energy"] = ["Energy must be between 1 and 5."];
        if (draft.Stress is < 1 or > 5) errors["stress"] = ["Stress must be between 1 and 5."];
        if (NormalizeTags(draft.Tags).Count > MaxTags)
            errors["tags"] = [$"Use at most {MaxTags} tags."];
        if (draft.Tags?.Any(tag => tag?.Trim().Length > MaxTagLength) == true)
            errors["tags"] = [$"Tags can contain at most {MaxTagLength} characters."];

        var hasContent = !string.IsNullOrWhiteSpace(draft.Content) || !string.IsNullOrWhiteSpace(draft.Highlights) ||
                         !string.IsNullOrWhiteSpace(draft.Gratitude) ||
                         draft.Rating is not null || draft.Mood is not null || draft.Energy is not null ||
                         draft.Stress is not null;
        if (!hasContent && errors.Count == 0)
            errors["content"] = ["Write something or add a rating before saving."];
        return errors;
    }

    /// <summary>Lowercases, trims, strips a leading '#', and removes duplicates while keeping order.</summary>
    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? tags) =>
        tags is null
            ? []
            : tags.Select(tag => tag?.Trim().TrimStart('#').Trim().ToLowerInvariant())
                .Where(tag => !string.IsNullOrEmpty(tag))
                .Select(tag => tag!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Renders a journal entry as the memory text that Jarvis searches and recalls.</summary>
public static class JournalMemoryFormatter
{
    public const string MemoryKind = "journal";
    public const string MemorySourceType = "journal";
    private const int MaxMemoryLength = 8_000;

    public static string Format(JournalEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append("Journal entry for ")
            .Append(entry.EntryDate.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture));
        var ratings = new List<string>();
        if (entry.Rating is { } rating) ratings.Add($"day rating {rating}/10");
        if (entry.Mood is { } mood) ratings.Add($"mood {mood}/5");
        if (entry.Energy is { } energy) ratings.Add($"energy {energy}/5");
        if (entry.Stress is { } stress) ratings.Add($"stress {stress}/5 (5 is most stressed)");
        if (ratings.Count > 0) builder.Append(" (").Append(string.Join(", ", ratings)).Append(')');
        builder.Append('.');
        if (!string.IsNullOrWhiteSpace(entry.Highlights)) builder.Append("\nHighlights: ").Append(entry.Highlights.Trim());
        if (!string.IsNullOrWhiteSpace(entry.Gratitude)) builder.Append("\nGrateful for: ").Append(entry.Gratitude.Trim());
        if (entry.Tags.Count > 0) builder.Append("\nTags: ").Append(string.Join(", ", entry.Tags));
        if (!string.IsNullOrWhiteSpace(entry.Content)) builder.Append('\n').Append(entry.Content.Trim());
        var text = builder.ToString();
        return text.Length <= MaxMemoryLength ? text : text[..(MaxMemoryLength - 1)] + "…";
    }

    /// <summary>Importance rises slightly for entries with substantial text.</summary>
    public static float Importance(JournalEntry entry) => entry.Content.Length > 400 ? 0.6f : 0.5f;
}
