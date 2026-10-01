using System.Globalization;
using System.Text;
using Jarvis.Domain.Lists;

namespace Jarvis.Application.Lists;

public static class ListKinds
{
    public const string Shopping = "shopping";
    public const string Todo = "todo";
    public const string General = "general";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Shopping, Todo, General };

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? kind) =>
        kind is not null && All.Contains(kind);

    private static readonly string[] ShoppingWords =
        ["boodschap", "shopping", "grocer", "supermarkt", "supermarket", "winkel", "einkauf"];

    private static readonly string[] TodoWords = ["todo", "takenlijst", "klusjes", "klussen", "chores", "tasks"];

    /// <summary>Picks a kind from a list name such as "Boodschappen" or "To-do list"; general when unsure.</summary>
    public static string Guess(string? name)
    {
        var key = ListRules.NameKey(name);
        if (ShoppingWords.Any(key.Contains)) return Shopping;
        if (TodoWords.Any(key.Contains) || key is "taken" or "doen") return Todo;
        return General;
    }
}

public static class ListRules
{
    public const int MaxNameLength = 60;
    public const int MaxItemLength = 200;
    public const int MaxItemsPerList = 500;
    public const int MaxLists = 50;
    public const int MaxItemsPerRequest = 50;

    private static readonly HashSet<string> LeadingWords =
        new(StringComparer.Ordinal) { "my", "mijn", "the", "de", "het", "our", "onze", "ons" };

    private static readonly string[] NameSuffixes = ["lijstje", "lijsten", "lijst", "lists", "list"];

    public static string? ValidateName(string? name)
    {
        var clean = Clean(name);
        if (clean is null) return "Give the list a name.";
        if (clean.Length > MaxNameLength) return $"List names can contain at most {MaxNameLength} characters.";
        return NameKey(clean).Length == 0 ? "Give the list a name with letters or digits." : null;
    }

    public static string? ValidateItem(string? text)
    {
        var clean = Clean(text);
        if (clean is null) return "Items cannot be empty.";
        return clean.Length > MaxItemLength ? $"Items can contain at most {MaxItemLength} characters." : null;
    }

    /// <summary>Trims and collapses runs of whitespace; null when nothing is left.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Key that treats "Boodschappenlijst", "my groceries list" style variants of a name as the same list:
    /// lowercase, accents removed, leading "my"/"mijn"/"de" dropped, trailing "list"/"lijst" dropped, and only
    /// letters and digits kept.
    /// </summary>
    public static string NameKey(string? name)
    {
        var words = Words(name);
        while (words.Count > 1 && LeadingWords.Contains(words[0])) words.RemoveAt(0);
        var joined = string.Concat(words);
        foreach (var suffix in NameSuffixes)
        {
            if (joined.Length > suffix.Length && joined.EndsWith(suffix, StringComparison.Ordinal))
            {
                joined = joined[..^suffix.Length];
                break;
            }
        }
        return joined;
    }

    /// <summary>Key for comparing item text: lowercase words without accents or punctuation.</summary>
    public static string ItemKey(string? text) => string.Join(' ', Words(text));

    private static List<string> Words(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}

public enum ListFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>Outcome of a list change: the value, or why it failed and which field caused it.</summary>
public sealed record ListOperation<T>(T? Value, ListFailure Failure = ListFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == ListFailure.None;

    public static ListOperation<T> Ok(T value) => new(value);
    public static ListOperation<T> NotFound() => new(default, ListFailure.NotFound);

    public static ListOperation<T> Invalid(string field, string message) =>
        new(default, ListFailure.Invalid, field, message);

    public static ListOperation<T> Conflict(string field, string message) =>
        new(default, ListFailure.Conflict, field, message);
}

/// <summary>
/// Result of adding text items: <see cref="Added"/> are new, <see cref="Reopened"/> were checked off and are open
/// again, <see cref="AlreadyOnList"/> were already open and were left alone.
/// </summary>
public sealed record AddItemsResult(
    PersonalList List,
    bool CreatedList,
    IReadOnlyList<ListItem> Added,
    IReadOnlyList<ListItem> Reopened,
    IReadOnlyList<string> AlreadyOnList);

/// <summary>Result of matching item text to change or remove items.</summary>
public sealed record MatchItemsResult(
    PersonalList List,
    IReadOnlyList<ListItem> Changed,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<AmbiguousItem> Ambiguous);

public sealed record AmbiguousItem(string Text, IReadOnlyList<string> Candidates);

public interface IListRepository
{
    /// <summary>The owner's lists with their items, most recently changed first.</summary>
    Task<IReadOnlyList<PersonalList>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<PersonalList?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddListAsync(PersonalList list, CancellationToken cancellationToken);

    Task<bool> UpdateListAsync(Guid id, Guid ownerId, string name, string kind, DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<bool> DeleteListAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Adds new items and saves changed ones (text and done state) in one save, touching the list.</summary>
    Task<bool> SaveItemsAsync(Guid ownerId, Guid listId, IReadOnlyList<ListItem> added,
        IReadOnlyList<ListItem> changed, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    Task<int> DeleteItemsAsync(Guid ownerId, Guid listId, IReadOnlyCollection<Guid> itemIds,
        DateTimeOffset updatedAt, CancellationToken cancellationToken);
}

public interface IListService
{
    Task<IReadOnlyList<PersonalList>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<PersonalList?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a list by a spoken or typed name ("de boodschappenlijst", "groceries"). Falls back to the only list of
    /// the matching kind, so "shopping list" finds a list called "Boodschappen".
    /// </summary>
    Task<PersonalList?> FindAsync(Guid ownerId, string name, CancellationToken cancellationToken);

    Task<ListOperation<PersonalList>> CreateAsync(Guid ownerId, string? name, string? kind,
        CancellationToken cancellationToken);

    Task<ListOperation<PersonalList>> UpdateAsync(Guid id, Guid ownerId, string? name, string? kind,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Adds items, skipping ones already open and reopening ones that were checked off.</summary>
    Task<ListOperation<AddItemsResult>> AddItemsAsync(Guid ownerId, Guid listId, IEnumerable<string?> texts,
        CancellationToken cancellationToken);

    /// <summary>Finds the list by name, creating it when it does not exist yet, then adds the items.</summary>
    Task<ListOperation<AddItemsResult>> AddItemsByNameAsync(Guid ownerId, string listName,
        IEnumerable<string?> texts, CancellationToken cancellationToken);

    Task<ListOperation<ListItem>> UpdateItemAsync(Guid ownerId, Guid listId, Guid itemId, string? text, bool? done,
        CancellationToken cancellationToken);

    Task<bool> DeleteItemAsync(Guid ownerId, Guid listId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>Removes every checked-off item; null when the list does not exist.</summary>
    Task<int?> ClearDoneAsync(Guid ownerId, Guid listId, CancellationToken cancellationToken);

    /// <summary>Checks items off (or reopens them) by matching their text.</summary>
    Task<ListOperation<MatchItemsResult>> SetDoneByTextAsync(Guid ownerId, Guid listId, IEnumerable<string?> texts,
        bool done, CancellationToken cancellationToken);

    Task<ListOperation<MatchItemsResult>> RemoveByTextAsync(Guid ownerId, Guid listId, IEnumerable<string?> texts,
        CancellationToken cancellationToken);
}
