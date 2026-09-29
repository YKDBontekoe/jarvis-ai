using System.Text.Json;
using Jarvis.Application;
using Jarvis.Application.Integrations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Jarvis.Mcp;

public sealed partial class McpToolHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, LiveSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> InspectAsync(string? endpoint, string? serverId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(serverId))
            return "Pass either an endpoint or a registered server id, not both.";
        try
        {
            if (!string.IsNullOrWhiteSpace(serverId))
            {
                var resolved = await ResolveForUseAsync(serverId, allowPaused: true, cancellationToken);
                if (resolved.Error is not null) return resolved.Error;
                var credential = await ResolveCredentialsAsync(resolved.Server!, cancellationToken);
                if (!credential.Ready)
                {
                    return IntegrationCredentialProviders.IsUserMcpServerId(resolved.Server!.CredentialProvider)
                        ? FormatAuthorizationRequired(resolved.Server, resolved)
                        : FormatPendingHostCatalog(resolved.Server, resolved);
                }
                return await WithClientAsync(resolved.Server!, async (client, secrets) =>
                    FormatCatalog(await ReadCatalogAsync(client, cancellationToken), resolved, secrets), cancellationToken);
            }
            if (string.IsNullOrWhiteSpace(endpoint))
                return "Provide a public HTTPS MCP endpoint or a registered server id.";
            var validated = await McpServerEndpointValidator.ValidateAsync(endpoint, cancellationToken);
            var discovery = new McpServerOptions
            {
                Name = "MCP discovery",
                Transport = "streamableHttp",
                Endpoint = validated,
                ConnectionTimeoutSeconds = 20,
                CredentialProvider = "jarvis-mcp-discovery",
                AllowedTools = [McpToolSelection.All]
            };
            await using var client = await McpClient.CreateAsync(
                CreateTransport(discovery, loggerFactory), cancellationToken: cancellationToken);
            var catalog = await ReadCatalogAsync(client, cancellationToken);
            return FormatCatalog(catalog, new ResolvedMcpServer(discovery, true, false), []);
        }
        catch (ArgumentException exception) { return $"Could not inspect MCP server: {exception.Message}"; }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled) { throw canceled; }
        catch (Exception exception)
        {
            logger.LogWarning("MCP inspection failed ({FailureType}).", exception.GetType().Name);
            if (McpAuthorization.IsAuthorizationFailure(exception))
            {
                var target = !string.IsNullOrWhiteSpace(serverId) ? serverId : endpoint;
                var url = await McpAuthorizationDiscovery.DiscoverAuthorizationUrlAsync(endpoint, exception,
                    cancellationToken);
                return McpAuthorizationDiscovery.AuthorizationFailureMessage(target, target, url);
            }
            return "Could not inspect MCP server. It may require authentication or may be unavailable. Call RequestMcpAuthorization, then AskForMcpCredential if a token must be pasted. Keep credentials out of chat text.";
        }
    }

    public async Task<string> RequestAuthorizationAsync(string? server, string? endpoint,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(endpoint))
            return "Pass either a server id/name or an endpoint, not both.";
        if (!string.IsNullOrWhiteSpace(server))
        {
            var resolved = await ResolveForUseAsync(server, allowPaused: true, cancellationToken);
            if (resolved.Error is not null) return resolved.Error;
            var credential = await ResolveCredentialsAsync(resolved.Server!, cancellationToken);
            if (credential.Ready)
                return "This MCP server already has stored credentials. Discover its tools. If it still fails, call AskForMcpCredential so the user can rotate the token in this chat.";
            var url = await McpAuthorizationDiscovery.DiscoverAuthorizationUrlAsync(resolved.Server!.Endpoint, null,
                cancellationToken);
            return McpAuthorization.FormatAsk(resolved.Server.Name,
                string.IsNullOrWhiteSpace(resolved.Server.CredentialProvider)
                    ? resolved.Server.Name.ToLowerInvariant()
                    : resolved.Server.CredentialProvider,
                url, extra: "Ask the user now and wait until they finish authorization. Render an Authorize card or call AskForMcpCredential; do not send them to Settings.");
        }
        if (string.IsNullOrWhiteSpace(endpoint))
            return "Name the MCP server or pass its public HTTPS endpoint so Jarvis can request authorization.";
        try
        {
            var validated = await McpServerEndpointValidator.ValidateAsync(endpoint, cancellationToken);
            var url = await McpAuthorizationDiscovery.DiscoverAuthorizationUrlAsync(validated, null, cancellationToken);
            return McpAuthorization.FormatAsk(validated, null, url,
                extra: "Register the server after the user authorizes it.");
        }
        catch (ArgumentException exception)
        {
            return $"Could not request MCP authorization: {exception.Message}";
        }
    }

    public async Task<string> InvokeToolAsync(string serverKey, string toolName, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolName) || toolName.Length > 128 || toolName.Any(char.IsControl))
            return "Use the exact MCP tool name.";
        Dictionary<string, object?> arguments;
        try { arguments = ReadArguments(argumentsJson); }
        catch (ArgumentException exception) { return exception.Message; }
        catch (JsonException) { return "Tool arguments must be a JSON object."; }

        try
        {
            var resolved = await ResolveForUseAsync(serverKey, allowPaused: false, cancellationToken);
            if (resolved.Error is not null) return resolved.Error;
            if (!ToolPermitted(resolved.Server!.AllowedTools, toolName))
                return $"'{toolName}' is not enabled on this MCP server. Discover its tools, then add the exact name or *.";
            return await WithClientAsync(resolved.Server, async (client, secrets) =>
            {
                var available = await client.ListToolsAsync(cancellationToken: cancellationToken);
                var tool = available.FirstOrDefault(candidate => candidate.Name.Equals(toolName, StringComparison.Ordinal));
                if (tool is null) return "That MCP server does not expose this tool.";
                var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
                return McpSecretRedactor.Bound(McpSecretRedactor.Redact(JsonSerializer.Serialize(result, JsonOptions), secrets), 12_000);
            }, cancellationToken);
        }
        catch (ArgumentException exception) { return $"Could not call MCP tool: {exception.Message}"; }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled) { throw canceled; }
        catch (Exception exception)
        {
            logger.LogWarning("MCP tool {ToolName} failed ({FailureType}).", toolName, exception.GetType().Name);
            if (McpAuthorization.IsAuthorizationFailure(exception))
            {
                var url = await McpAuthorizationDiscovery.DiscoverAuthorizationUrlAsync(null, exception,
                    cancellationToken);
                return McpAuthorizationDiscovery.AuthorizationFailureMessage(serverKey, serverKey, url);
            }
            return "The MCP tool call failed. If it needs authorization, call RequestMcpAuthorization and try again.";
        }
    }

    public async Task<string> ReadResourceAsync(string serverKey, string uri, CancellationToken cancellationToken)
    {
        try { McpResourceUris.Validate(uri); }
        catch (ArgumentException exception) { return exception.Message; }
        try
        {
            var resolved = await ResolveForUseAsync(serverKey, allowPaused: false, cancellationToken);
            if (resolved.Error is not null) return resolved.Error;
            return await WithClientAsync(resolved.Server!, async (client, secrets) =>
            {
                var result = await client.ReadResourceAsync(uri.Trim(), cancellationToken: cancellationToken);
                return McpSecretRedactor.Bound(McpSecretRedactor.Redact(JsonSerializer.Serialize(result, JsonOptions), secrets), 12_000);
            }, cancellationToken);
        }
        catch (ArgumentException exception) { return $"Could not read MCP resource: {exception.Message}"; }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled) { throw canceled; }
        catch (Exception exception)
        {
            logger.LogWarning("MCP resource read failed ({FailureType}).", exception.GetType().Name);
            return "Could not read that MCP resource. The server may not expose it.";
        }
    }

    public async Task<string> GetPromptAsync(string serverKey, string name, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            return "Use the exact MCP prompt name.";
        Dictionary<string, object?> arguments;
        try { arguments = ReadArguments(argumentsJson); }
        catch (ArgumentException exception) { return exception.Message; }
        catch (JsonException) { return "Prompt arguments must be a JSON object."; }
        try
        {
            var resolved = await ResolveForUseAsync(serverKey, allowPaused: false, cancellationToken);
            if (resolved.Error is not null) return resolved.Error;
            return await WithClientAsync(resolved.Server!, async (client, secrets) =>
            {
                IEnumerable<McpClientPrompt> prompts;
                try { prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken); }
                catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is null)
                {
                    return "This MCP server does not expose prompts.";
                }
                var prompt = prompts.FirstOrDefault(candidate => candidate.Name.Equals(name.Trim(), StringComparison.Ordinal));
                if (prompt is null) return "This MCP server does not expose that prompt.";
                var result = await prompt.GetAsync(arguments, cancellationToken: cancellationToken);
                return McpSecretRedactor.Bound(McpSecretRedactor.Redact(JsonSerializer.Serialize(result, JsonOptions), secrets), 12_000);
            }, cancellationToken);
        }
        catch (ArgumentException exception) { return $"Could not fetch MCP prompt: {exception.Message}"; }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled) { throw canceled; }
        catch (Exception exception)
        {
            logger.LogWarning("MCP prompt {PromptName} failed ({FailureType}).", name, exception.GetType().Name);
            return "Could not fetch that MCP prompt.";
        }
    }

    private async Task<bool> IsToolPermittedAsync(string serverKey, string toolName, CancellationToken cancellationToken)
    {
        var resolved = await ResolveForUseAsync(serverKey, allowPaused: true, cancellationToken);
        return resolved.Error is null && resolved.Enabled && resolved.Server is not null &&
            ToolPermitted(resolved.Server.AllowedTools, toolName);
    }

    private async Task<ResolvedMcpServer> ResolveForUseAsync(string serverKey, bool allowPaused,
        CancellationToken cancellationToken)
    {
        var key = serverKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
            return ResolvedMcpServer.Fail("Name the MCP server by its id or configured name.");
        var owned = await userMcpServers.ListAsync(currentUser.OwnerId, cancellationToken);
        UserMcpServer? userServer = IntegrationCredentialProviders.IsUserMcpServerId(key)
            ? owned.FirstOrDefault(server => server.Id == key)
            : null;
        if (userServer is null && !IntegrationCredentialProviders.IsUserMcpServerId(key) &&
            McpServerConfiguration.Find(configuration, key) is null)
        {
            var named = owned.Where(server => server.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (named.Length > 1)
                return ResolvedMcpServer.Fail("More than one MCP server has that name. Use the server id.");
            userServer = named.FirstOrDefault();
        }
        if (userServer is not null)
        {
            if (!userServer.Enabled && !allowPaused)
                return ResolvedMcpServer.Fail($"MCP server '{userServer.Name}' is paused. Enable it before using it.");
            var options = await CreateUserServerOptionsAsync(userServer, cancellationToken);
            return new ResolvedMcpServer(options, userServer.Enabled, true);
        }
        if (IntegrationCredentialProviders.IsUserMcpServerId(key))
            return ResolvedMcpServer.Fail("That MCP server was not found.");

        var configured = McpServerConfiguration.Find(configuration, key);
        if (configured is null) return ResolvedMcpServer.Fail("That MCP server was not found.");
        var policy = await ownerPolicy.GetAsync(currentUser.OwnerId, cancellationToken);
        var enabled = true;
        if (policy.TryGetValue(configured.Name, out var host))
        {
            enabled = host.Enabled;
            if (!enabled && !allowPaused)
                return ResolvedMcpServer.Fail($"MCP server '{configured.Name}' is paused. Enable it before using it.");
            if (host.AllowedTools is { Count: > 0 })
            {
                configured.OwnerNarrowed = true;
                configured.AllowedTools = McpToolSelection.Restrict(configured.AllowedTools, host.AllowedTools);
            }
        }
        return new ResolvedMcpServer(configured, enabled, true);
    }

    private async Task<string> WithClientAsync(McpServerOptions server,
        Func<McpClient, string[], Task<string>> action, CancellationToken cancellationToken)
    {
        if (TryGetSession(server, out var session) && session is not null)
            return await action(session.Client, session.Secrets);
        var credential = await ResolveCredentialsAsync(server, cancellationToken);
        if (!credential.Ready)
            return FormatAuthorizationRequired(server, new ResolvedMcpServer(server, true, true));
        if (IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider))
        {
            if (server.Transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase))
                await McpServerEndpointValidator.ValidateAsync(server.Endpoint, cancellationToken);
        }
        var client = await McpClient.CreateAsync(CreateTransport(server, loggerFactory), cancellationToken: cancellationToken);
        var secrets = SecretValues(credential.Secrets, server);
        var live = new LiveSession(client, secrets);
        _clients.Add(client);
        Remember(server, live);
        return await action(client, secrets);
    }

    private bool TryGetSession(McpServerOptions server, out LiveSession? session)
    {
        session = null;
        if (IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) &&
            _sessions.TryGetValue(server.CredentialProvider!, out session))
            return session is not null;
        return _sessions.TryGetValue(server.Name, out session) && session is not null;
    }

    private void Remember(McpServerOptions server, LiveSession session)
    {
        _sessions[server.Name] = session;
        if (UserServerId(server) is { } id) _sessions[id] = session;
    }

    private async Task<McpServerCatalog> ReadCatalogAsync(McpClient client, CancellationToken cancellationToken)
    {
        var tools = (await client.ListToolsAsync(cancellationToken: cancellationToken))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
        var truncated = tools.Length > McpToolSelection.MaxTools;
        var selected = tools.Take(McpToolSelection.MaxTools).Select(tool => new McpCatalogTool(tool.Name,
            Clip(tool.Description, 300), RequiredArguments(tool))).ToArray();
        var prompts = await TryListAsync(async () =>
            (await client.ListPromptsAsync(cancellationToken: cancellationToken))
                .OrderBy(prompt => prompt.Name, StringComparer.Ordinal)
                .Select(prompt => new McpCatalogPrompt(prompt.Name, Clip(prompt.Description, 300))), 40);
        var resources = await TryListAsync(async () =>
            (await client.ListResourcesAsync(cancellationToken: cancellationToken))
                .OrderBy(resource => resource.Name, StringComparer.Ordinal)
                .Select(resource => new McpCatalogResource(resource.Uri, Clip(resource.Name, 120),
                    Clip(resource.Description, 300))), 40);
        if (prompts.Truncated || resources.Truncated) truncated = true;
        string? instructions = null;
        try { instructions = Clip(client.ServerInstructions, 800); }
        catch (InvalidOperationException) { }
        return new McpServerCatalog(tools.Length, selected, prompts.Items, resources.Items, instructions, truncated);
    }

    private static async Task<(IReadOnlyList<T> Items, bool Truncated)> TryListAsync<T>(
        Func<Task<IEnumerable<T>>> load, int limit)
    {
        try
        {
            var items = (await load()).Take(limit + 1).ToArray();
            return (items.Take(limit).ToArray(), items.Length > limit);
        }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled)
        {
            throw canceled;
        }
        catch (Exception)
        {
            return ([], false);
        }
    }

    private static string FormatPendingHostCatalog(McpServerOptions server, ResolvedMcpServer resolved)
    {
        var provider = string.IsNullOrWhiteSpace(server.CredentialProvider)
            ? server.Name.ToLowerInvariant()
            : server.CredentialProvider!;
        var configuredTools = McpToolSelection.AllowsAll(server.AllowedTools)
            ? new[] { McpToolSelection.All }
            : server.AllowedTools;
        return JsonSerializer.Serialize(new
        {
            registered = resolved.Registered,
            enabled = resolved.Registered && resolved.Enabled,
            hostManaged = true,
            transport = server.Transport,
            credentialProvider = provider,
            hasCredentials = false,
            state = "needs_credentials",
            askUser = true,
            mustAsk = true,
            operatorAllowlist = configuredTools,
            enabledTools = configuredTools,
            allowsAllTools = McpToolSelection.AllowsAll(configuredTools),
            toolCount = 0,
            tools = Array.Empty<object>(),
            authorization = JsonSerializer.Deserialize<JsonElement>(
                McpAuthorization.FormatAsk(server.Name, provider)),
            nextStep = $"Ask the user to authorize '{server.Name}' now. Call AskForMcpCredential with provider '{provider}', then discover again with serverId '{server.Name}'."
        }, JsonOptions);
    }

    private static string FormatAuthorizationRequired(McpServerOptions server, ResolvedMcpServer resolved)
    {
        var provider = string.IsNullOrWhiteSpace(server.CredentialProvider)
            ? server.Name.ToLowerInvariant()
            : server.CredentialProvider!;
        return McpAuthorization.FormatAsk(server.Name, provider,
            extra: resolved.Registered
                ? "The server is registered but is not authorized yet."
                : "Authorize this server before discovering or invoking its tools.");
    }

    private static string FormatCatalog(McpServerCatalog catalog, ResolvedMcpServer resolved, string[] secrets)
    {
        var enabledTools = resolved.Registered && resolved.Server is not null ? resolved.Server.AllowedTools : [];
        var json = JsonSerializer.Serialize(new
        {
            registered = resolved.Registered,
            enabled = resolved.Registered && resolved.Enabled,
            allowsAllTools = McpToolSelection.AllowsAll(enabledTools),
            enabledTools = McpToolSelection.AllowsAll(enabledTools) ? [] : enabledTools,
            toolCount = catalog.ToolCount,
            tools = catalog.Tools,
            prompts = catalog.Prompts,
            resources = catalog.Resources,
            instructions = catalog.Instructions,
            truncated = catalog.Truncated
        }, JsonOptions);
        return McpSecretRedactor.Redact(json, secrets);
    }

    private static IReadOnlyList<string> RequiredArguments(AIFunction tool)
    {
        var schema = tool.JsonSchema;
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
            return [];
        return required.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!).Take(12).ToArray();
    }

    private static Dictionary<string, object?> ReadArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>(StringComparer.Ordinal);
        if (json.Length > 16_000) throw new ArgumentException("Arguments must be at most 16000 characters.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Arguments must be a JSON object.");
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            arguments[property.Name] = property.Value.Clone();
        return arguments;
    }

    private static bool ToolPermitted(IReadOnlyList<string> allowed, string toolName) =>
        McpToolSelection.AllowsAll(allowed) || allowed.Contains(toolName, StringComparer.Ordinal);

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private sealed record LiveSession(McpClient Client, string[] Secrets);

    private sealed record ResolvedMcpServer(McpServerOptions? Server, bool Enabled, bool Registered, string? Error = null)
    {
        public static ResolvedMcpServer Fail(string error) => new(null, false, false, error);
    }

    private sealed record McpCatalogTool(string Name, string? Description, IReadOnlyList<string> RequiredArguments);
    private sealed record McpCatalogPrompt(string Name, string? Description);
    private sealed record McpCatalogResource(string Uri, string? Name, string? Description);
    private sealed record McpServerCatalog(int ToolCount, IReadOnlyList<McpCatalogTool> Tools,
        IReadOnlyList<McpCatalogPrompt> Prompts, IReadOnlyList<McpCatalogResource> Resources,
        string? Instructions, bool Truncated);

    private sealed class GuardedMcpTool(AIFunction inner, McpToolHost host, string serverKey, string toolName)
        : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            if (!await host.IsToolPermittedAsync(serverKey, toolName, cancellationToken))
                throw new InvalidOperationException($"'{toolName}' is not enabled on this MCP server.");
            return await base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }
}
