using System.Text.Json;
using Jarvis.Application.Integrations;

namespace Jarvis.Infrastructure.Persistence;

public sealed class OwnerMcpPolicyStore(IIntegrationCredentialStore credentials) : IOwnerMcpPolicyStore
{
    public const string Provider = "jarvis-host-mcp-policy";
    private const string Secret = "policy";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, HostMcpOverride>> GetAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var secrets = await credentials.GetSecretsAsync(ownerId, Provider, cancellationToken);
        if (secrets is null || !secrets.TryGetValue(Secret, out var json) || string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, HostMcpOverride>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, StoredOverride>>(json, JsonOptions);
            if (stored is null) return new Dictionary<string, HostMcpOverride>(StringComparer.OrdinalIgnoreCase);
            return stored.ToDictionary(pair => pair.Key,
                pair => new HostMcpOverride(pair.Value.Enabled ?? true, pair.Value.AllowedTools),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, HostMcpOverride>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<HostMcpOverride> SaveAsync(Guid ownerId, string serverName, HostMcpOverride value,
        CancellationToken cancellationToken)
    {
        var name = serverName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80)
            throw new ArgumentException("Use the configured MCP server name.");
        var all = new Dictionary<string, StoredOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, existing) in await GetAsync(ownerId, cancellationToken))
            all[key] = new StoredOverride(existing.Enabled, existing.AllowedTools?.ToArray());
        all[name] = new StoredOverride(value.Enabled, value.AllowedTools?.ToArray());
        await credentials.SaveSecretAsync(ownerId, Provider, Secret, JsonSerializer.Serialize(all, JsonOptions),
            cancellationToken);
        return value;
    }

    private sealed record StoredOverride(bool? Enabled, string[]? AllowedTools);
}
