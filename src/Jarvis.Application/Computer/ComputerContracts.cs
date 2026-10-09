namespace Jarvis.Application.Computer;

/// <summary>The sandbox's control endpoints that are not MCP tools.</summary>
public interface IComputerSandbox
{
    /// <summary>False when the <c>computer</c> feature is off; UseComputer is then not offered.</summary>
    bool IsConfigured { get; }

    /// <summary>How long an unused session keeps the sandbox before another conversation may take it.</summary>
    TimeSpan IdleTimeout { get; }

    /// <summary>Closes the browser and every program and wipes the sandbox user's files.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

public static class ComputerScreenshots
{
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Screenshots kept per session; older ones are deleted as new ones arrive.</summary>
    public const int KeptPerSession = 30;

    public static string NewKey(Guid ownerId, Guid sessionId, string mediaType) =>
        $"computer-screenshots/{ownerId:N}/{sessionId:N}/{Guid.CreateVersion7():N}.{Extension(mediaType)}";

    public static bool BelongsTo(string? key, Guid ownerId, Guid sessionId) =>
        key is not null && key.StartsWith($"computer-screenshots/{ownerId:N}/{sessionId:N}/", StringComparison.Ordinal);

    public static string MediaTypeOf(string key) => key.EndsWith(".png", StringComparison.Ordinal)
        ? "image/png"
        : key.EndsWith(".webp", StringComparison.Ordinal) ? "image/webp" : "image/jpeg";

    private static string Extension(string mediaType) => mediaType switch
    {
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "jpg"
    };
}
