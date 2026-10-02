namespace Jarvis.Domain.Reading;

/// <summary>
/// A link the owner saved to read later. Jarvis fetches the page in the background and fills in
/// <see cref="Title"/>, <see cref="Summary"/>, <see cref="KeyPoints"/>, and <see cref="ReadingMinutes"/>.
/// <see cref="Status"/> is "pending" until that finishes, then "ready" or "failed". The page text itself is not kept.
/// </summary>
public sealed record ReadingItem(
    Guid Id,
    Guid OwnerId,
    string Url,
    string Status,
    string? Title,
    string? SiteName,
    string? Excerpt,
    string? Summary,
    IReadOnlyList<string> KeyPoints,
    int? WordCount,
    int? ReadingMinutes,
    string? Note,
    string Source,
    string? FailureReason,
    int FetchAttempts,
    DateTimeOffset? NextFetchAt,
    DateTimeOffset? FetchedAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsRead => ReadAt is not null;

    /// <summary>The title when known, otherwise the host name, so the item always has a label.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title :
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : Url;
}
