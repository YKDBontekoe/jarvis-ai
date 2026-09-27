namespace Jarvis.Application.Integrations;

public sealed record IntegrationCredentialStatus(string Provider, IReadOnlyList<string> SecretNames,
    DateTimeOffset UpdatedAt);

public static class IntegrationCredentialProviders
{
    public const string ReservedPrefix = "jarvis-";
    public const string UserMcpPrefix = "jarvis-mcp-";
    public const string OpenRouter = "jarvis-model-openrouter";
    public const string ChannelPrefix = "jarvis-channel-";
    public const string RemoteAgentPrefix = "jarvis-agent-";
    public const string UserMcpConfigSecret = "server_config";
    public const string UserMcpTokenSecret = "token";

    public static bool IsUserMcpManaged(string? provider) =>
        !string.IsNullOrEmpty(provider) &&
        provider.StartsWith(UserMcpPrefix, StringComparison.Ordinal);

    /// <summary>Providers whose secrets belong to a Jarvis feature screen rather than the generic credential API.</summary>
    public static bool IsSystemManaged(string? provider) =>
        IsReserved(provider) && !IsUserMcpManaged(provider);

    public static bool IsReserved(string? provider) =>
        !string.IsNullOrEmpty(provider) &&
        provider.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    public static bool IsUserMcpTokenSecret(string? secretName) =>
        string.Equals(secretName, UserMcpTokenSecret, StringComparison.Ordinal);
}

public interface IIntegrationCredentialStore
{
    Task<IReadOnlyList<IntegrationCredentialStatus>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IntegrationCredentialStatus?> GetStatusAsync(Guid ownerId, string provider, CancellationToken cancellationToken);
    Task SaveAsync(Guid ownerId, string provider, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken);
    Task SaveSecretAsync(Guid ownerId, string provider, string secretName, string value,
        CancellationToken cancellationToken);
    Task<bool> DeleteSecretAsync(Guid ownerId, string provider, string secretName,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>?> GetSecretsAsync(Guid ownerId, string provider,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, string provider, CancellationToken cancellationToken);
}
