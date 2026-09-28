using System.Text.Json;
using System.Text.RegularExpressions;
using Jarvis.Application.Integrations;

namespace Jarvis.Infrastructure.Persistence;

public sealed partial class UserMcpServerRegistry(IIntegrationCredentialStore credentials) : IUserMcpServerRegistry
{
    private const string ProviderPrefix = IntegrationCredentialProviders.UserMcpPrefix;
    private const string ConfigSecret = IntegrationCredentialProviders.UserMcpConfigSecret;
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
                    result.Add(ToPublic(provider.Provider, stored, provider.UpdatedAt,
                        provider.SecretNames.Contains(IntegrationCredentialProviders.UserMcpTokenSecret,
                            StringComparer.Ordinal)));
            }
            catch (JsonException) { }
        }
        return result.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<UserMcpServer> AddAsync(Guid ownerId, AddUserMcpServerRequest request,
        CancellationToken cancellationToken)
    {
        var id = ProviderPrefix + Guid.NewGuid().ToString("N");
        var stored = await BuildHttpStoredAsync(request, cancellationToken);
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret,
            JsonSerializer.Serialize(stored, JsonOptions), cancellationToken);
        return ToPublic(id, stored, DateTimeOffset.UtcNow, false);
    }

    public async Task<UserMcpServer> AddStdioAsync(Guid ownerId, AddUserMcpStdioServerRequest request,
        CancellationToken cancellationToken)
    {
        var id = ProviderPrefix + Guid.NewGuid().ToString("N");
        var stored = BuildStdioStored(request);
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret,
            JsonSerializer.Serialize(stored, JsonOptions), cancellationToken);
        return ToPublic(id, stored, DateTimeOffset.UtcNow, false);
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
        StoredServer? current = null;
        try { current = JsonSerializer.Deserialize<StoredServer>(existing[ConfigSecret], JsonOptions); }
        catch (JsonException) { }
        var replacement = await BuildHttpStoredAsync(request, cancellationToken);
        replacement = replacement with { Enabled = current?.Enabled ?? true };
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret,
            JsonSerializer.Serialize(replacement, JsonOptions), cancellationToken);
        var status = await credentials.GetStatusAsync(ownerId, id, cancellationToken);
        return ToPublic(id, replacement, status?.UpdatedAt ?? DateTimeOffset.UtcNow,
            status?.SecretNames.Contains(IntegrationCredentialProviders.UserMcpTokenSecret, StringComparer.Ordinal) == true);
    }

    public async Task<UserMcpServer?> SetEnabledAsync(Guid ownerId, string id, bool enabled,
        CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(ownerId, id, cancellationToken);
        if (stored is null) return null;
        var updated = stored.Value.Server with { Enabled = enabled };
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret, JsonSerializer.Serialize(updated, JsonOptions),
            cancellationToken);
        return ToPublic(id, updated, DateTimeOffset.UtcNow, stored.Value.HasToken);
    }

    public async Task<UserMcpServer?> SetToolsAsync(Guid ownerId, string id, string mode, IReadOnlyList<string> allowedTools,
        CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(ownerId, id, cancellationToken);
        if (stored is null) return null;
        var tools = McpToolSelection.Apply(stored.Value.Server.AllowedTools, mode, allowedTools);
        var endpoint = stored.Value.Server.Transport is "stdio"
            ? stored.Value.Server.Endpoint
            : await McpServerEndpointValidator.ValidateAsync(stored.Value.Server.Endpoint, cancellationToken);
        var updated = stored.Value.Server with { AllowedTools = tools, Endpoint = endpoint };
        await credentials.SaveSecretAsync(ownerId, id, ConfigSecret, JsonSerializer.Serialize(updated, JsonOptions),
            cancellationToken);
        return ToPublic(id, updated, DateTimeOffset.UtcNow, stored.Value.HasToken);
    }

    private async Task<(StoredServer Server, bool HasToken)?> ReadStoredAsync(Guid ownerId, string id,
        CancellationToken cancellationToken)
    {
        if (!IsId(id)) return null;
        var secrets = await credentials.GetSecretsAsync(ownerId, id, cancellationToken);
        if (secrets is null || !secrets.TryGetValue(ConfigSecret, out var json)) return null;
        var stored = JsonSerializer.Deserialize<StoredServer>(json, JsonOptions);
        return stored is null ? null : (stored, secrets.ContainsKey(IntegrationCredentialProviders.UserMcpTokenSecret));
    }

    private static async Task<StoredServer> BuildHttpStoredAsync(AddUserMcpServerRequest request,
        CancellationToken cancellationToken)
    {
        var name = ValidateName(request.Name);
        var allowedTools = McpToolSelection.Normalize(request.AllowedTools);
        return new StoredServer(name, await McpServerEndpointValidator.ValidateAsync(request.Endpoint, cancellationToken),
            allowedTools, true, "streamableHttp", null, [], null);
    }

    private static StoredServer BuildStdioStored(AddUserMcpStdioServerRequest request)
    {
        var name = ValidateName(request.Name);
        var allowedTools = McpToolSelection.Normalize(request.AllowedTools);
        var (command, arguments) = McpStdioCommandValidator.Normalize(request.Command, request.Arguments);
        return new StoredServer(name, string.Empty, allowedTools, true, "stdio", command, arguments, null);
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 80 || !NamePattern().IsMatch(trimmed))
            throw new ArgumentException("Use a server name of 1 to 80 letters, numbers, spaces, underscores, or hyphens.");
        return trimmed;
    }

    public static bool IsId(string? id) => IntegrationCredentialProviders.IsUserMcpServerId(id);

    private static UserMcpServer ToPublic(string id, StoredServer stored, DateTimeOffset updatedAt, bool hasToken) =>
        new(id, stored.Name, stored.Endpoint, stored.AllowedTools, updatedAt, hasToken, stored.Enabled ?? true,
            stored.Transport ?? "streamableHttp", stored.Command, stored.Arguments);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
    private sealed record StoredServer(string Name, string Endpoint, string[] AllowedTools, bool? Enabled,
        string? Transport = null, string? Command = null, string[]? Arguments = null,
        Dictionary<string, string>? CredentialEnvironmentVariables = null);
}
