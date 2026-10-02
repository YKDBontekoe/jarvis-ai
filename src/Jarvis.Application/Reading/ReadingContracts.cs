using System.Net;
using Jarvis.Application.Browser;
using Jarvis.Domain.Reading;

namespace Jarvis.Application.Reading;

public static class ReadingStatuses
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public static class ReadingSources
{
    public const string App = "app";
    public const string Chat = "chat";

    public static string Normalize(string? source) => source == Chat ? Chat : App;
}

public static class ReadingRules
{
    public const int MaxUrlLength = 2_000;
    public const int MaxNoteLength = 500;
    public const int MaxItems = 1_000;
    public const int MaxFetchAttempts = 3;
    public const int WordsPerMinute = 230;
    public const int MaxSummaryLength = 1_200;
    public const int MaxKeyPoints = 5;
    public const int MaxKeyPointLength = 240;
    public const int MaxTitleLength = 300;
    public const int MaxExcerptLength = 400;

    /// <summary>Minutes to read <paramref name="words"/> at an average pace, at least one.</summary>
    public static int ReadingMinutes(int words) => Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute,
        MidpointRounding.AwayFromZero));

    public static string? ValidateNote(string? note) =>
        note is not null && note.Trim().Length > MaxNoteLength
            ? $"Notes can contain at most {MaxNoteLength} characters."
            : null;

    public static string? CleanNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

/// <summary>
/// Checks links before they are saved or fetched. Everything the isolated browser refuses is refused here too, and
/// on top of that IP literals, intranet-style names and ports other than 80 and 443.
/// </summary>
public static class ReadingUrls
{
    private static readonly string[] TrackingParameters =
        ["fbclid", "gclid", "mc_cid", "mc_eid", "igshid", "ref_src", "si"];

    /// <summary>The cleaned absolute URL, or an <see cref="ArgumentException"/> explaining why it cannot be saved.</summary>
    public static string Normalize(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("Paste a link to save.");
        if (trimmed.Length > ReadingRules.MaxUrlLength)
            throw new ArgumentException($"Links can contain at most {ReadingRules.MaxUrlLength} characters.");
        if (!trimmed.Contains("://", StringComparison.Ordinal)) trimmed = "https://" + trimmed;
        var normalized = BrowserUrls.Normalize(trimmed)!;
        var uri = new Uri(normalized);
        if (!uri.IsDefaultPort) throw new ArgumentException("Only links on the standard web ports can be saved.");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out _) || IsLocalName(uri.IdnHost))
            throw new ArgumentException("The reading list cannot open local or private hosts.");
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        builder.Query = string.Join('&', (uri.Query.TrimStart('?')).Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(pair.Split('=')[0])));
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// Key that treats http/https, "www.", a trailing slash and letter case in the host as the same link, so a link
    /// is not saved twice.
    /// </summary>
    public static string Key(string normalizedUrl)
    {
        var uri = new Uri(normalizedUrl);
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        var path = uri.AbsolutePath.TrimEnd('/');
        return host + path + uri.Query;
    }

    public static bool IsLocalName(string host)
    {
        var normalized = host.TrimEnd('.');
        return normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
               !normalized.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsTracking(string name) =>
        name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
        TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
}

public enum ReadingFailure
{
    None,
    NotFound,
    Invalid
}

/// <summary>Outcome of a reading-list change: the value, or why it failed and which field caused it.</summary>
public sealed record ReadingOperation<T>(T? Value, ReadingFailure Failure = ReadingFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == ReadingFailure.None;

    public static ReadingOperation<T> Ok(T value) => new(value);
    public static ReadingOperation<T> NotFound() => new(default, ReadingFailure.NotFound);

    public static ReadingOperation<T> Invalid(string field, string message) =>
        new(default, ReadingFailure.Invalid, field, message);
}

/// <summary>A saved link and whether it was already on the list before this save.</summary>
public sealed record SaveReadingResult(ReadingItem Item, bool AlreadySaved);

/// <summary>Text pulled from a fetched page. <see cref="FinalUrl"/> is where redirects ended.</summary>
public sealed record FetchedPage(string FinalUrl, string? Title, string? SiteName, string? Description, string Text);

/// <summary>Why a page could not be read. Permanent failures are not retried.</summary>
public sealed class ReadingFetchException(string message, bool permanent) : Exception(message)
{
    public bool Permanent { get; } = permanent;
}

public sealed record ReadingSummary(string Summary, IReadOnlyList<string> KeyPoints);

/// <summary>Downloads a public web page for the reading list, refusing local and private addresses.</summary>
public interface IReadingPageFetcher
{
    /// <exception cref="ReadingFetchException">When the page cannot be read.</exception>
    Task<FetchedPage> FetchAsync(string url, CancellationToken cancellationToken);
}

/// <summary>Writes a short summary of a page. Returns null when no model is available or the answer is unusable.</summary>
public interface IReadingSummarizer
{
    Task<ReadingSummary?> SummarizeAsync(Guid ownerId, FetchedPage page, CancellationToken cancellationToken);
}

public interface IReadingRepository
{
    /// <summary>The owner's saved links, newest first.</summary>
    Task<IReadOnlyList<ReadingItem>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<ReadingItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<ReadingItem?> FindByKeyAsync(Guid ownerId, string urlKey, CancellationToken cancellationToken);
    Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(ReadingItem item, string urlKey, CancellationToken cancellationToken);

    /// <summary>Saves every mutable field of <paramref name="item"/>; false when it no longer exists.</summary>
    Task<bool> SaveAsync(ReadingItem item, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Pending items of any owner whose next fetch is due, oldest first. Only the background worker calls this.</summary>
    Task<IReadOnlyList<ReadingItem>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}

public interface IReadingListService
{
    Task<IReadOnlyList<ReadingItem>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ReadingItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a link for later. A link that is already saved is returned as is (and put back on the unread list when it
    /// was read), so sharing the same article twice does not create a duplicate.
    /// </summary>
    Task<ReadingOperation<SaveReadingResult>> SaveAsync(Guid ownerId, string? url, string? note, string? source,
        CancellationToken cancellationToken);

    Task<ReadingOperation<ReadingItem>> UpdateAsync(Guid id, Guid ownerId, bool? read, string? note,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Queues the page to be fetched and summarized again.</summary>
    Task<ReadingOperation<ReadingItem>> RefreshAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Finds saved links by URL or by words in the title, best match first.</summary>
    Task<IReadOnlyList<ReadingItem>> FindAsync(Guid ownerId, string query, CancellationToken cancellationToken);
}
