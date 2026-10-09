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
    IReadOnlyList<BrowserStepRecord> Steps,
    string Kind = BrowserSessionKinds.Browser,
    string ControlMode = ComputerControlModes.Agent);

/// <param name="ScreenshotKey">Object-storage key of the screen after this step (computer sessions only).</param>
public sealed record BrowserStepRecord(Guid Id, int Ordinal, string Tool, string Summary, bool Success,
    DateTimeOffset CreatedAt, string? ScreenshotKey = null);

public static class BrowserSessionKinds
{
    /// <summary>The headless Playwright browser (BrowseTheWeb).</summary>
    public const string Browser = "browser";

    /// <summary>The computer-use sandbox desktop (UseComputer).</summary>
    public const string Computer = "computer";
}

public static class ComputerControlModes
{
    /// <summary>Jarvis drives the sandbox.</summary>
    public const string Agent = "agent";

    /// <summary>The owner took over from the live view; Jarvis's computer tools wait.</summary>
    public const string User = "user";

    public static bool IsKnown(string? mode) => mode is Agent or User;
}

public interface IBrowserSessionStore
{
    Task<BrowserSessionRecord> StartAsync(Guid ownerId, Guid conversationId, string goal, string? startUrl,
        CancellationToken cancellationToken);
    Task<BrowserSessionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<BrowserSessionRecord?> GetActiveForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserSessionRecord>> ListForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task<BrowserStepRecord?> RecordStepAsync(Guid sessionId, string tool, string summary, bool success,
        CancellationToken cancellationToken, string? screenshotKey = null);
    Task CompleteAsync(Guid sessionId, string status, CancellationToken cancellationToken);

    /// <summary>
    /// Claims the one computer-use sandbox for a conversation. Returns null while another conversation holds an
    /// active computer session that was used within <paramref name="idleTimeout"/>; older ones are expired.
    /// </summary>
    Task<BrowserSessionRecord?> TryStartComputerAsync(Guid ownerId, Guid conversationId, string goal,
        string? startUrl, TimeSpan idleTimeout, CancellationToken cancellationToken);

    /// <summary>Forgets all but the newest <paramref name="keep"/> screenshots of a session and returns the dropped keys.</summary>
    Task<IReadOnlyList<string>> TrimScreenshotsAsync(Guid sessionId, int keep, CancellationToken cancellationToken);

    /// <summary>Changes who drives an active computer session. False when it is not the owner's active session.</summary>
    Task<bool> SetControlModeAsync(Guid ownerId, Guid sessionId, string controlMode,
        CancellationToken cancellationToken);
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
