using Jarvis.Domain.Library;

namespace Jarvis.Application.Library;

public static class LibraryKinds
{
    public const string Web = "web";
    public const string Note = "note";
    public const string Report = "report";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? kind) =>
        kind is Web or Note or Report;
}

public static class LibraryOrigins
{
    public const string App = "app";
    public const string Chat = "chat";
    public const string Research = "research";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? origin) =>
        origin is App or Chat or Research;
}

public static class LibraryRules
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 800;
    public const int MaxContentLength = 60_000;
    public const int MaxKeyPoints = 8;
    public const int MaxKeyPointLength = 240;
    public const int MaxTags = 8;
    public const int MaxTagLength = 32;
    public const int MaxCardsPerItem = 12;
    public const int MaxCardTextLength = 400;
    public const int MaxFetchBytes = 2 * 1024 * 1024;
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 200;

    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? Limit(string? value, int max)
    {
        var clean = Clean(value);
        return clean is null ? null : clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Content keeps its line breaks but not stray control characters, cut to the maximum length.</summary>
    public static string CleanContent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Replace("\t", " ");
        return text.Length <= MaxContentLength ? text.Trim() : text[..MaxContentLength].TrimEnd();
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? tags) =>
        tags is null
            ? []
            : tags.Select(x => x?.Trim().TrimStart('#').Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrEmpty(x) && x.Length <= MaxTagLength)
                .Select(x => x!).Distinct(StringComparer.Ordinal).Take(MaxTags).ToArray();

    /// <summary>Drops the fragment and trailing slash and lowercases the host, so a link is not saved twice.</summary>
    public static string? NormalizeUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"https://{uri.IdnHost.ToLowerInvariant()}{path}{uri.Query}";
    }
}

/// <summary>A page fetched from the public web.</summary>
public sealed record FetchedPage(string FinalUrl, string ContentType, string Body);

/// <summary>Raised when a page cannot be fetched; the message is safe to show to the owner.</summary>
public sealed class WebFetchException(string message) : Exception(message);

/// <summary>Fetches public HTTPS pages without letting a link reach the owner's network.</summary>
public interface IWebPageFetcher
{
    Task<FetchedPage> FetchAsync(string url, CancellationToken cancellationToken);
}

/// <summary>What a model (or the fallback) makes of a page or a note.</summary>
public sealed record LibraryDigestResult(
    string Summary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CardDraft> Cards);

public sealed record CardDraft(string Front, string Back);

public interface ILibraryDigester
{
    Task<LibraryDigestResult> DigestAsync(Guid ownerId, string title, string text, CancellationToken cancellationToken);
}

public sealed record LibraryQuery(string? Text = null, string? Tag = null, string? Kind = null,
    int Limit = LibraryRules.DefaultListLimit);

public sealed record CardStats(int Total, int Due, int New, int Learned);

public interface ILibraryRepository
{
    Task<IReadOnlyList<LibraryItem>> ListAsync(Guid ownerId, LibraryQuery query, CancellationToken cancellationToken);
    Task<LibraryItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<LibraryItem?> FindByUrlAsync(Guid ownerId, string normalizedUrl, CancellationToken cancellationToken);
    Task AddAsync(LibraryItem item, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(LibraryItem item, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryItem>> ListSinceAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Flashcard>> ListCardsAsync(Guid ownerId, Guid? itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Flashcard>> ListDueCardsAsync(Guid ownerId, DateOnly today, int limit,
        CancellationToken cancellationToken);
    Task<Flashcard?> GetCardAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddCardsAsync(IReadOnlyList<Flashcard> cards, CancellationToken cancellationToken);
    Task<bool> UpdateCardAsync(Flashcard card, CancellationToken cancellationToken);
    Task<bool> DeleteCardAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<int> CountCardsAsync(Guid ownerId, CancellationToken cancellationToken);
}

public enum LibraryFailure
{
    None,
    NotFound,
    Invalid,
    Unavailable
}

public sealed record LibraryOperation<T>(T? Value, LibraryFailure Failure = LibraryFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == LibraryFailure.None;

    public static LibraryOperation<T> Ok(T value) => new(value);
    public static LibraryOperation<T> NotFound() => new(default, LibraryFailure.NotFound);
    public static LibraryOperation<T> Invalid(string field, string message) =>
        new(default, LibraryFailure.Invalid, field, message);
    public static LibraryOperation<T> Unavailable(string message) =>
        new(default, LibraryFailure.Unavailable, null, message);
}

public sealed record NoteDraft(string? Title, string? Content, string Kind = LibraryKinds.Note, string? Url = null,
    IReadOnlyList<string>? Tags = null, string Origin = LibraryOrigins.App, Guid? ProjectId = null,
    bool MakeCards = false);

public sealed record LibraryDigestReport(
    int Days,
    IReadOnlyList<LibraryItem> Items,
    CardStats Cards,
    IReadOnlyList<string> TopTags);

public sealed record ResearchStarted(JarvisTaskRecordLite Task);

public sealed record JarvisTaskRecordLite(Guid Id, string Title);

public interface ILibraryService
{
    Task<IReadOnlyList<LibraryItem>> ListAsync(Guid ownerId, LibraryQuery query, CancellationToken cancellationToken);
    Task<LibraryItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Fetches a public page, reads it, and keeps it with a summary, tags, and flashcards.</summary>
    Task<LibraryOperation<LibraryItem>> ClipAsync(Guid ownerId, string? url, string? note, Guid? projectId,
        string origin, CancellationToken cancellationToken);

    Task<LibraryOperation<LibraryItem>> AddNoteAsync(Guid ownerId, NoteDraft draft, CancellationToken cancellationToken);
    Task<LibraryOperation<LibraryItem>> SetTagsAsync(Guid id, Guid ownerId, IReadOnlyList<string> tags,
        CancellationToken cancellationToken);
    Task<LibraryDigestReport> DigestAsync(Guid ownerId, int days, DateOnly today, DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Flashcard>> DueCardsAsync(Guid ownerId, DateOnly today, int limit,
        CancellationToken cancellationToken);
    Task<CardStats> CardStatsAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<IReadOnlyList<Flashcard>> AddCardsAsync(Guid ownerId, Guid? itemId, IReadOnlyList<CardDraft> cards,
        DateOnly today, CancellationToken cancellationToken);
    Task<LibraryOperation<Flashcard>> ReviewAsync(Guid cardId, Guid ownerId, int quality, DateOnly today,
        CancellationToken cancellationToken);
    Task<bool> DeleteCardAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken);
}

public interface IResearchService
{
    /// <summary>Starts a durable background task that researches the question and saves a cited report.</summary>
    Task<LibraryOperation<ResearchStarted>> StartAsync(Guid ownerId, string? question, string? depth, Guid? projectId,
        CancellationToken cancellationToken);
}
