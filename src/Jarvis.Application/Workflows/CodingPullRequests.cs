namespace Jarvis.Application.Workflows;

/// <summary>One file touched by a coding run. <see cref="Protected"/> marks security-sensitive areas a human should read closely.</summary>
public sealed record CodingDiffFile(string Path, string Status, bool Protected);

public sealed record CodingDiff(string Patch, IReadOnlyList<CodingDiffFile> Files, bool Truncated,
    IReadOnlyList<string> Warnings);

public sealed record CodingCheck(string Name, string Status, string? Conclusion);

/// <summary>State of the pull request opened for a coding run, refreshed from the code host.</summary>
public sealed record CodingPullRequestStatus(int Number, string Url, string Repository, string Title, string State,
    bool Merged, bool? Mergeable, string? MergeableState, string? HeadSha, string ChecksState,
    IReadOnlyList<CodingCheck> Checks);

/// <summary>A pull-request operation that cannot proceed; <see cref="Code"/> is stable for clients.</summary>
public sealed class CodingPullRequestException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Turns a finished coding run into a reviewable pull request and lets the owner merge or close it.
/// Merging is only reachable from an explicit owner action, never from an agent tool.
/// </summary>
public interface ICodingPullRequestService
{
    /// <summary>Whether the run's repository can publish pull requests (a GitHub repository is configured).</summary>
    bool CanPublish(string repository);

    Task<CodingDiff> GetDiffAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken);

    Task<CodingPullRequestStatus> PublishAsync(Guid runId, Guid ownerId, string? title, string? body,
        CancellationToken cancellationToken);

    Task<CodingPullRequestStatus?> GetStatusAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken);

    Task<CodingPullRequestStatus> MergeAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken);

    Task<CodingPullRequestStatus> CloseAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>Which files a self-authored change may touch.</summary>
public static class CodingPathPolicy
{
    /// <summary>Paths a coding run may never publish: CI definitions, git internals, and credential files.</summary>
    public static bool IsBlocked(string path)
    {
        var normalized = Normalize(path);
        if (normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(".gitmodules", StringComparison.OrdinalIgnoreCase))
            return true;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var name = segments.LastOrDefault() ?? string.Empty;
        if (segments.Any(segment => segment.Equals("secrets", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".ssh", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (name.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) &&
             !name.EndsWith(".example", StringComparison.OrdinalIgnoreCase)))
            return true;
        var extension = Path.GetExtension(name);
        return extension.Equals(".pem", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".key", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("auth.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Code that guards access or approvals; allowed, but shown to the reviewer with a warning.</summary>
    public static bool IsProtected(string path)
    {
        var normalized = Normalize(path);
        string[] markers =
        [
            "/approvals/", "/approval", "/identity/", "/security/", "authentication", "currentuser",
            "/migrations/", "program.cs", "appsettings", "dockerfile", "docker-compose", "infra/",
            "/jarvis.mcp/", "credential"
        ];
        var lower = "/" + normalized.ToLowerInvariant();
        return markers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
