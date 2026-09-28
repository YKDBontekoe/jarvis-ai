using System.Text.Json;
using Jarvis.Application.Integrations;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Mcp;

public sealed partial class McpToolHost
{
    private static readonly JsonSerializerOptions HostCatalogJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> ListHostServersAsync(CancellationToken cancellationToken)
    {
        var policy = await ownerPolicy.GetAsync(currentUser.OwnerId, cancellationToken);
        var entries = new List<object>();
        foreach (var server in McpServerConfiguration.Read(configuration))
        {
            if (string.IsNullOrWhiteSpace(server.Name)) continue;
            var provider = CredentialProviderSlug(server);
            var secrets = await credentialStore.GetSecretsAsync(currentUser.OwnerId, provider, cancellationToken);
            policy.TryGetValue(server.Name, out var hostPolicy);
            var enabled = hostPolicy?.Enabled ?? true;
            var configuredTools = McpToolSelection.AllowsAll(server.AllowedTools)
                ? new[] { McpToolSelection.All }
                : server.AllowedTools;
            var ownerTools = hostPolicy?.AllowedTools is { Count: > 0 } ownerFilter
                ? McpToolSelection.Restrict(server.AllowedTools, ownerFilter)
                : configuredTools;
            entries.Add(new
            {
                name = server.Name,
                transport = server.Transport,
                credentialProvider = provider,
                hasCredentials = secrets is not null,
                hostManaged = true,
                enabled,
                operatorAllowlist = configuredTools,
                ownerAllowlist = ownerTools,
                registerWith = "Use SetMcpServerEnabled and SetMcpServerTools with this name. Do not use AddMcpServer for host servers."
            });
        }
        return JsonSerializer.Serialize(entries, HostCatalogJsonOptions);
    }

    private async Task<McpServerConnectionStatus> BuildStatusAsync(McpServerOptions server, string state, int toolCount,
        string? issue, bool enabled, IReadOnlyList<string>? tools, CancellationToken cancellationToken)
    {
        var hostManaged = !IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider);
        var provider = hostManaged ? CredentialProviderSlug(server) : server.CredentialProvider;
        var configuredTools = server.AllowedTools.Length == 0
            ? null
            : McpToolSelection.AllowsAll(server.AllowedTools)
                ? (IReadOnlyList<string>)[McpToolSelection.All]
                : server.AllowedTools;
        var secrets = provider is null
            ? null
            : await credentialStore.GetSecretsAsync(currentUser.OwnerId, provider, cancellationToken);
        return new McpServerConnectionStatus(server.Name, state, toolCount, issue, UserServerId(server), enabled, tools,
            hostManaged, server.Transport, provider, configuredTools, secrets is not null);
    }

    private static string CredentialProviderSlug(McpServerOptions server) =>
        string.IsNullOrWhiteSpace(server.CredentialProvider)
            ? server.Name.ToLowerInvariant()
            : server.CredentialProvider!;
}
