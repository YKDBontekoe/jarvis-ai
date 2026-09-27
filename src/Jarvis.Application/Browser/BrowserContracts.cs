namespace Jarvis.Application.Browser;

public sealed record BrowserSessionRecord(
    Guid Id,
    Guid OwnerId,
    Guid ConversationId,
    string Goal,
    string? StartUrl,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BrowserStepRecord> Steps);

public sealed record BrowserStepRecord(Guid Id, int Ordinal, string Tool, string Summary, bool Success,
    DateTimeOffset CreatedAt);

public interface IBrowserSessionStore
{
    Task<BrowserSessionRecord> StartAsync(Guid ownerId, Guid conversationId, string goal, string? startUrl,
        CancellationToken cancellationToken);
    Task<BrowserSessionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<BrowserSessionRecord?> GetActiveForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserSessionRecord>> ListForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task RecordStepAsync(Guid sessionId, string tool, string summary, bool success,
        CancellationToken cancellationToken);
    Task CompleteAsync(Guid sessionId, string status, CancellationToken cancellationToken);
}

public static class BrowserUrls
{
    public static string? Normalize(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(parsed.UserInfo))
            throw new ArgumentException("Start from an http(s) URL without embedded credentials.");
        if (parsed.Host is "localhost" or "127.0.0.1" || parsed.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The isolated browser cannot open local or private hosts.");
        return parsed.ToString();
    }
}
