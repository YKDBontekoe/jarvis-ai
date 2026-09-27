using System.Text.RegularExpressions;

namespace Jarvis.Application.Agents;

public sealed record RemoteAgentRecord(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Url,
    bool Enabled,
    DateTimeOffset? LastUsedAt,
    string? LastError,
    DateTimeOffset CreatedAt);

public sealed record SaveRemoteAgentRequest(string? Name, string? Url, bool Enabled, string? Token);

public sealed record A2ATokenRecord(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt,
    string? Token = null);

public interface IRemoteAgentRepository
{
    Task<IReadOnlyList<RemoteAgentRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<RemoteAgentRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<RemoteAgentRecord> CreateAsync(Guid ownerId, string name, string url, bool enabled,
        CancellationToken cancellationToken);
    Task<RemoteAgentRecord?> UpdateAsync(Guid ownerId, Guid id, string name, string url, bool enabled,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task RecordUseAsync(Guid ownerId, Guid id, string? error, CancellationToken cancellationToken);
}

public interface IA2ATokenRepository
{
    Task<IReadOnlyList<A2ATokenRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<(A2ATokenRecord Record, string Token)> CreateAsync(Guid ownerId, string name,
        CancellationToken cancellationToken);
    Task<Guid?> FindOwnerAsync(string token, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
}

public static partial class RemoteAgentValidation
{
    public const int MaxName = 80;

    public static (string Name, string Url) Normalize(string? name, string? url)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > MaxName)
            throw new ArgumentException("Give the agent a name of 1 to 80 characters.");
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(parsed.UserInfo))
            throw new ArgumentException("Enter an http(s) Agent2Agent endpoint without embedded credentials.");
        if (parsed.Scheme == "http" && parsed.Host is not ("localhost" or "127.0.0.1"))
            throw new ArgumentException("Remote agents must use HTTPS except on localhost.");
        return (trimmedName, parsed.ToString().TrimEnd('/'));
    }

    public static string NormalizeTokenName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxName) throw new ArgumentException("Give the token a short name.");
        if (!NamePattern().IsMatch(trimmed))
            throw new ArgumentException("Token names may use letters, digits, spaces, and - _ .");
        return trimmed;
    }

    [GeneratedRegex(@"^[A-Za-z0-9 ._:-]{1,80}$")]
    private static partial Regex NamePattern();
}
