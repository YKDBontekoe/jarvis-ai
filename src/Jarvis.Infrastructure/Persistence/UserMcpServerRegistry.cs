using System.Text.Json;
using System.Text.RegularExpressions;
using Jarvis.Application.Integrations;

namespace Jarvis.Infrastructure.Persistence;

public sealed partial class UserMcpServerRegistry(IIntegrationCredentialStore credentials) : IUserMcpServerRegistry
{
    private const string ProviderPrefix = IntegrationCredentialProviders.UserMcpPrefix;
    private const string ConfigSecret = "server_config";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<UserMcpServer>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var result = new List<UserMcpServer>();
        foreach (var provider in await credentials.ListAsync(ownerId, cancellationToken))
        {
            if (!provider.Provider.StartsWith(ProviderPrefix, StringComparison.Ordinal) ||
                !provider.SecretNames.Contains(ConfigSecret, StringComparer.Ordinal)) continue;
            var secrets = await credentials.GetSecretsAsync(ownerId, provider.Provider, cancellationToken);
            if (secrets is null || !secrets.TryGetValue(ConfigSecret, out var json)) continue;
            try
            {
                var stored = JsonSerializer.Deserialize<StoredServer>(json, JsonOptions);
                if (stored is not null)
                    result.Add(ToPublic(provider.Provider, stored, provider.UpdatedAt));
            }
            catch (JsonException) { }
        }
        return result.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<UserMcpServer> AddAsync(Guid ownerId, AddUserMcpServerRequest request,
        CancellationToken cancellationToken)
    {
        var id = ProviderPrefix + Guid.NewGuid().ToString("N");
        var stored = await BuildStoredAsync(request, cancellationToken);
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret,
            JsonSerializer.Serialize(stored, JsonOptions), cancellationToken);
        return ToPublic(id, stored, DateTimeOffset.UtcNow);
    }

    public async Task<bool> RemoveAsync(Guid ownerId, string id, CancellationToken cancellationToken)
    {
        if (!IsId(id)) return false;
        var secrets = await credentials.GetSecretsAsync(ownerId, id, cancellationToken);
        if (secrets is null || !secrets.ContainsKey(ConfigSecret)) return false;
        await credentials.DeleteAsync(ownerId, id, cancellationToken);
        return true;
    }

    public async Task<UserMcpServer?> UpdateAsync(Guid ownerId, string id, AddUserMcpServerRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsId(id)) return null;
        var existing = await credentials.GetSecretsAsync(ownerId, id, cancellationToken);
        if (existing is null || !existing.ContainsKey(ConfigSecret)) return null;
        var replacement = await BuildStoredAsync(request, cancellationToken);
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret,
            JsonSerializer.Serialize(replacement, JsonOptions), cancellationToken);
        var status = await credentials.GetStatusAsync(ownerId, id, cancellationToken);
        return ToPublic(id, replacement, status?.UpdatedAt ?? DateTimeOffset.UtcNow);
    }

    private static async Task<StoredServer> BuildStoredAsync(AddUserMcpServerRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var allowedTools = request.AllowedTools?.Select(tool => tool?.Trim() ?? string.Empty)
            .Where(tool => tool.Length > 0).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (name.Length is < 1 or > 80 || !NamePattern().IsMatch(name))
            throw new ArgumentException("Use a server name of 1 to 80 letters, numbers, spaces, underscores, or hyphens.");
        if (allowedTools.Length is < 1 or > 50 || allowedTools.Any(tool => tool.Length > 128 || !ToolPattern().IsMatch(tool)))
            throw new ArgumentException("Add 1 to 50 exact tool names using letters, numbers, underscores, dots, or hyphens.");
        return new StoredServer(name, await McpServerEndpointValidator.ValidateAsync(request.Endpoint, cancellationToken), allowedTools);
    }

    public static bool IsId(string id) => id.StartsWith(ProviderPrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(id[ProviderPrefix.Length..], "N", out _);

    private static UserMcpServer ToPublic(string id, StoredServer stored, DateTimeOffset updatedAt) =>
        new(id, stored.Name, stored.Endpoint, stored.AllowedTools, updatedAt);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolPattern();
    private sealed record StoredServer(string Name, string Endpoint, string[] AllowedTools);
}
