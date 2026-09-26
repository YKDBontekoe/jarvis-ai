namespace Jarvis.Application.Integrations;

public sealed record IntegrationCredentialStatus(string Provider, IReadOnlyList<string> SecretNames,
    DateTimeOffset UpdatedAt);

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
