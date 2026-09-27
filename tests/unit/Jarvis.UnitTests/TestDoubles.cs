using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
using Microsoft.Extensions.AI;

namespace Jarvis.UnitTests;

internal sealed class FixedChatClientResolver(IChatClient client, EmbeddingModel? embeddings = null) : IChatClientResolver
{
    public List<(Guid OwnerId, ModelPurpose Purpose)> Requests { get; } = [];

    public Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose, CancellationToken cancellationToken)
    {
        Requests.Add((ownerId, purpose));
        return Task.FromResult(client);
    }

    public Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(embeddings);
}

internal sealed class InMemorySettingsStore : IOwnerSettingsStore
{
    private readonly Dictionary<(Guid, string), object> _values = [];

    public Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken cancellationToken) where T : class =>
        Task.FromResult(_values.TryGetValue((ownerId, section), out var value) ? (T)value : null);

    public Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken cancellationToken) where T : class
    {
        _values[(ownerId, section)] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(_values.Keys.Where(key => key.Item2 == section)
            .Select(key => key.Item1).Distinct().ToArray());
}

internal sealed class InMemoryCredentialStore : IIntegrationCredentialStore
{
    private readonly Dictionary<(Guid, string), Dictionary<string, string>> _secrets = [];

    public Task<IReadOnlyList<IntegrationCredentialStatus>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IntegrationCredentialStatus>>(_secrets
            .Where(pair => pair.Key.Item1 == ownerId)
            .Select(pair => new IntegrationCredentialStatus(pair.Key.Item2, pair.Value.Keys.ToArray(), DateTimeOffset.UtcNow))
            .ToArray());

    public Task<IntegrationCredentialStatus?> GetStatusAsync(Guid ownerId, string provider,
        CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.TryGetValue((ownerId, provider), out var values)
            ? new IntegrationCredentialStatus(provider, values.Keys.ToArray(), DateTimeOffset.UtcNow)
            : null);

    public Task SaveAsync(Guid ownerId, string provider, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken)
    {
        _secrets[(ownerId, provider)] = new Dictionary<string, string>(secrets);
        return Task.CompletedTask;
    }

    public Task SaveSecretAsync(Guid ownerId, string provider, string secretName, string value,
        CancellationToken cancellationToken)
    {
        if (!_secrets.TryGetValue((ownerId, provider), out var values))
            _secrets[(ownerId, provider)] = values = [];
        values[secretName] = value;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteSecretAsync(Guid ownerId, string provider, string secretName,
        CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.TryGetValue((ownerId, provider), out var values) && values.Remove(secretName));

    public Task<IReadOnlyDictionary<string, string>?> GetSecretsAsync(Guid ownerId, string provider,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(_secrets.TryGetValue((ownerId, provider), out var values)
            ? values
            : null);

    public Task<bool> DeleteAsync(Guid ownerId, string provider, CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.Remove((ownerId, provider)));
}
