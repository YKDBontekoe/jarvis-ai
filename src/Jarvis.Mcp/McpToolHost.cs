using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Jarvis.Application;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using System.Net;
using System.Net.Sockets;

namespace Jarvis.Mcp;

public sealed partial class McpToolHost(IConfiguration configuration, ILogger<McpToolHost> logger,
    ILoggerFactory loggerFactory,
    ICurrentUser currentUser, IIntegrationCredentialStore credentialStore,
    IUserMcpServerRegistry userMcpServers, IOwnerMcpPolicyStore ownerPolicy) : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _clients = [];
    private IReadOnlyList<AITool> _tools = [];
    private IReadOnlyList<McpServerConnectionStatus> _statuses = [];
    private bool _initialized;

    public IReadOnlyList<AITool> Tools => _tools;
    public IReadOnlyList<McpServerConnectionStatus> Statuses => _statuses;

    public async Task<IReadOnlyList<string>> DiscoverToolsAsync(string endpoint,
        CancellationToken cancellationToken)
    {
        var validatedEndpoint = await McpServerEndpointValidator.ValidateAsync(endpoint, cancellationToken);
        var server = new McpServerOptions
        {
            Name = "MCP discovery",
            Transport = "streamableHttp",
            Endpoint = validatedEndpoint,
            ConnectionTimeoutSeconds = 20,
            CredentialProvider = "jarvis-mcp-discovery"
        };
        try
        {
            await using var client = await McpClient.CreateAsync(
                CreateTransport(server, loggerFactory), cancellationToken: cancellationToken);
            var availableTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return availableTools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled)
        {
            throw canceled;
        }
        catch (Exception exception) when (McpAuthorization.IsAuthorizationFailure(exception))
        {
            throw new McpAuthorizationRequiredException(validatedEndpoint, exception);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        var policy = await ownerPolicy.GetAsync(currentUser.OwnerId, cancellationToken);
        var configuredServers = McpServerConfiguration.Read(configuration).ToList();
        var tools = new List<AITool>();
        var toolNames = new HashSet<string>(StringComparer.Ordinal);
        var statuses = new List<McpServerConnectionStatus>();
        foreach (var userServer in await userMcpServers.ListAsync(currentUser.OwnerId, cancellationToken))
        {
            if (!userServer.IsValid)
            {
                statuses.Add(new McpServerConnectionStatus(userServer.Name, "unavailable", 0,
                    userServer.ConfigurationIssue, userServer.Id, false, []));
                continue;
            }
            if (!userServer.Enabled)
            {
                statuses.Add(new McpServerConnectionStatus(userServer.Name, "paused", 0, "paused_by_owner",
                    userServer.Id, false, userServer.AllowedTools));
                continue;
            }
            configuredServers.Add(await CreateUserServerOptionsAsync(userServer, cancellationToken));
        }

        var servers = new List<McpServerOptions>();
        foreach (var server in configuredServers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                logger.LogWarning("Skipping an MCP server with no configured name.");
                statuses.Add(new McpServerConnectionStatus("(unnamed)", "unavailable", 0, "invalid_configuration"));
                continue;
            }
            if (!IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) &&
                policy.TryGetValue(server.Name, out var hostPolicy))
            {
                if (!hostPolicy.Enabled)
                {
                    statuses.Add(await BuildStatusAsync(server, "paused", 0, "paused_by_owner", false, null,
                        cancellationToken));
                    continue;
                }
                if (hostPolicy.AllowedTools is { Count: > 0 })
                {
                    server.AllowedTools = McpToolSelection.Restrict(server.AllowedTools, hostPolicy.AllowedTools);
                    server.OwnerNarrowed = true;
                }
            }
            if (server.AllowedTools.Length == 0)
            {
                logger.LogWarning("Skipping MCP server {ServerName}: no tools are explicitly allowlisted.", server.Name);
                statuses.Add(await BuildStatusAsync(server, "disabled", 0,
                    server.OwnerNarrowed ? "no_matching_tools" : "no_tools_allowlisted", true, [],
                    cancellationToken));
                continue;
            }
            servers.Add(server);
        }

        var connections = await Task.WhenAll(servers.Select(server =>
            ConnectServerAsync(server, cancellationToken)));

        foreach (var connection in connections)
        {
            if (connection.Client is null)
            {
                statuses.Add(connection.Status);
                continue;
            }
            var selected = new List<AITool>();
            foreach (var tool in connection.Tools ?? [])
            {
                if (toolNames.Add(tool.Name)) selected.Add(tool);
                else
                    logger.LogWarning("Skipping duplicate MCP tool {ToolName} from server {ServerName}; use InvokeMcpTool to reach it.",
                        tool.Name, connection.Server.Name);
            }
            tools.AddRange(selected);
            _clients.Add(connection.Client);
            Remember(connection.Server, connection.Session!);
            statuses.Add(connection.Status with
            {
                ToolCount = selected.Count,
                Tools = selected.Select(tool => tool.Name).ToArray()
            });
        }

        _tools = tools;
        _statuses = statuses;
        _initialized = true;
    }

    private sealed record ServerConnection(McpServerOptions Server, McpServerConnectionStatus Status,
        McpClient? Client = null, LiveSession? Session = null, IReadOnlyList<AITool>? Tools = null);

    private async Task<ServerConnection> ConnectServerAsync(McpServerOptions server,
        CancellationToken cancellationToken)
    {
        McpClient? client = null;
        try
        {
            var credentialResolution = await ResolveCredentialsAsync(server, cancellationToken);
            if (!credentialResolution.Ready)
                return new ServerConnection(server, await BuildStatusAsync(server, "needs_credentials", 0,
                    "credentials_required", true, null, cancellationToken));
            var serverSecrets = credentialResolution.Secrets;
            if (IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) &&
                server.Transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase))
                await McpServerEndpointValidator.ValidateAsync(server.Endpoint, cancellationToken);

            client = await McpClient.CreateAsync(CreateTransport(server, loggerFactory), cancellationToken: cancellationToken);
            var availableTools = (await client.ListToolsAsync(cancellationToken: cancellationToken)).ToList();
            var allowAllTools = McpToolSelection.AllowsAll(server.AllowedTools);
            var availableNames = availableTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
            HashSet<string> allowed;
            if (allowAllTools)
            {
                allowed = availableNames;
                if (allowed.Count > McpToolSelection.MaxTools)
                {
                    logger.LogWarning("MCP server {ServerName} exposes {ToolCount} tools; enabling the first {MaxTools}.",
                        server.Name, availableNames.Count, McpToolSelection.MaxTools);
                    allowed = availableNames.Order(StringComparer.Ordinal)
                        .Take(McpToolSelection.MaxTools).ToHashSet(StringComparer.Ordinal);
                }
            }
            else
            {
                allowed = server.AllowedTools.ToHashSet(StringComparer.Ordinal);
                var missing = allowed.Except(availableNames, StringComparer.Ordinal).ToArray();
                if (missing.Length != 0)
                    logger.LogWarning("MCP server {ServerName} no longer provides allowlisted tools: {Tools}.",
                        server.Name, string.Join(", ", missing));
                allowed.IntersectWith(availableNames);
                if (allowed.Count == 0)
                {
                    await CloseQuietlyAsync(client);
                    return new ServerConnection(server, await BuildStatusAsync(server, "disabled", 0,
                        "no_matching_tools", true, [], cancellationToken));
                }
            }
            var autoApproved = server.AutoApprovedTools.ToHashSet(StringComparer.Ordinal);
            autoApproved.IntersectWith(allowed);
            var transportSecrets = SecretValues(serverSecrets, server);
            var serverKey = UserServerId(server) ?? server.Name;
            var selected = availableTools.Where(tool => allowed.Contains(tool.Name)).Select(tool =>
            {
                AIFunction protectedFunction = transportSecrets.Length == 0
                    ? tool
                    : new SecretRedactingAIFunction(tool, transportSecrets);
                protectedFunction = new GuardedMcpTool(protectedFunction, this, serverKey, tool.Name);
                return autoApproved.Contains(tool.Name)
                    ? (AITool)protectedFunction
                    : new ApprovalRequiredAIFunction(protectedFunction);
            }).ToArray();

            var status = await BuildStatusAsync(server, "connected", selected.Length, null, true,
                selected.Select(tool => tool.Name).ToArray(), cancellationToken);
            logger.LogInformation("Connected MCP server {ServerName}; enabled {ToolCount} tools.", server.Name, selected.Length);
            return new ServerConnection(server, status, client, new LiveSession(client, transportSecrets), selected);
        }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled)
        {
            if (client is not null) await CloseQuietlyAsync(client);
            throw canceled;
        }
        catch (Exception exception)
        {
            if (client is not null) await CloseQuietlyAsync(client);
            var authorization = McpAuthorization.IsAuthorizationFailure(exception);
            var issue = authorization ? "authorization_required"
                : exception is ArgumentException or InvalidOperationException ? "invalid_configuration"
                : "server_unavailable";
            logger.LogWarning("Skipping MCP server {ServerName} after initialization failure ({FailureType}).",
                server.Name, exception.GetType().Name);
            return new ServerConnection(server, await BuildStatusAsync(server,
                authorization ? "needs_credentials" : "unavailable", 0, issue, true, null, cancellationToken));
        }
    }

    private async Task<McpServerOptions> CreateUserServerOptionsAsync(UserMcpServer userServer,
        CancellationToken cancellationToken)
    {
        var secrets = await credentialStore.GetSecretsAsync(currentUser.OwnerId, userServer.Id, cancellationToken);
        var transport = string.IsNullOrWhiteSpace(userServer.Transport) ? "streamableHttp" : userServer.Transport;
        var server = new McpServerOptions
        {
            Name = userServer.Name,
            Transport = transport,
            Endpoint = userServer.Endpoint,
            Command = userServer.Command ?? string.Empty,
            Arguments = userServer.Arguments?.ToArray() ?? [],
            AllowedTools = userServer.AllowedTools.ToArray(),
            CredentialProvider = userServer.Id,
            ConnectionTimeoutSeconds = 30
        };
        if (transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) &&
            secrets?.ContainsKey("token") == true)
        {
            server.CredentialHeaders["Authorization"] = "token";
            server.CredentialHeaderPrefixes["Authorization"] = "Bearer";
        }
        return server;
    }

    private static string? UserServerId(McpServerOptions server) =>
        IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) ? server.CredentialProvider : null;

    private static string[] SecretValues(IReadOnlyDictionary<string, string>? secrets, McpServerOptions server) =>
        server.Headers.Values.Concat(server.EnvironmentVariables.Values).Concat(secrets?.Values ?? [])
            .Where(value => !string.IsNullOrEmpty(value)).Select(value => value!).Distinct(StringComparer.Ordinal).ToArray();

    private static async Task CloseQuietlyAsync(IAsyncDisposable client)
    {
        try { await client.DisposeAsync(); }
        catch { /* Keep the owning agent run available when an optional server fails. */ }
    }

    private async Task<(bool Ready, IReadOnlyDictionary<string, string>? Secrets)> ResolveCredentialsAsync(
        McpServerOptions server, CancellationToken cancellationToken)
    {
        if (server.CredentialEnvironmentVariables.Count == 0 && server.CredentialHeaders.Count == 0)
            return (true, null);

        var provider = string.IsNullOrWhiteSpace(server.CredentialProvider)
            ? server.Name.ToLowerInvariant()
            : server.CredentialProvider;
        var secrets = await credentialStore.GetSecretsAsync(currentUser.OwnerId, provider, cancellationToken);
        if (secrets is null)
        {
            logger.LogInformation("Skipping MCP server {ServerName}; owner {OwnerId} has not configured its credentials.",
                server.Name, currentUser.OwnerId);
            return (false, null);
        }

        foreach (var (environmentName, secretName) in server.CredentialEnvironmentVariables)
        {
            if (!IsEnvironmentName(environmentName) || !secrets.TryGetValue(secretName, out var secret))
                throw new InvalidOperationException($"MCP server '{server.Name}' references a missing or invalid environment credential mapping.");
            server.EnvironmentVariables[environmentName] = secret;
        }

        foreach (var (headerName, secretName) in server.CredentialHeaders)
        {
            if (!IsHeaderName(headerName) || !secrets.TryGetValue(secretName, out var secret))
                throw new InvalidOperationException($"MCP server '{server.Name}' references a missing or invalid HTTP header credential mapping.");
            if (secret.Any(char.IsControl))
                throw new InvalidOperationException($"MCP server '{server.Name}' has an invalid HTTP header credential value.");
            var headerValue = secret;
            if (server.CredentialHeaderPrefixes.TryGetValue(headerName, out var prefix))
            {
                if (!IsHeaderPrefix(prefix))
                    throw new InvalidOperationException($"MCP server '{server.Name}' has an invalid HTTP header credential prefix.");
                headerValue = prefix + " " + secret;
            }
            server.Headers[headerName] = headerValue;
        }

        return (true, secrets);
    }

    private static IClientTransport CreateTransport(McpServerOptions server, ILoggerFactory loggerFactory)
    {
        if (server.ConnectionTimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException($"MCP server '{server.Name}' connection timeout must be between 1 and 300 seconds.");

        return server.Transport.Trim().ToLowerInvariant() switch
        {
            "stdio" => CreateStdioTransport(server),
            "streamablehttp" => CreateHttpTransport(server, loggerFactory),
            var transport => throw new InvalidOperationException(
                $"MCP server '{server.Name}' has unsupported transport '{transport}'. Use 'stdio' or 'streamableHttp'.")
        };
    }

    private static IClientTransport CreateStdioTransport(McpServerOptions server)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
            throw new InvalidOperationException($"MCP server '{server.Name}' using stdio requires a command.");

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var (name, value) in server.EnvironmentVariables)
            environment[name] = value;

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Arguments,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            WorkingDirectory = string.IsNullOrWhiteSpace(server.WorkingDirectory) ? null : server.WorkingDirectory
        });
    }

    private static IClientTransport CreateHttpTransport(McpServerOptions server, ILoggerFactory loggerFactory)
    {
        if (!Uri.TryCreate(server.Endpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new InvalidOperationException(
                $"MCP server '{server.Name}' using Streamable HTTP requires an absolute http(s) endpoint without embedded credentials.");
        if (server.CredentialHeaders.Count != 0 && endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"MCP server '{server.Name}' must use HTTPS when sending owner credentials in HTTP headers.");
        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(server.ConnectionTimeoutSeconds),
            AdditionalHeaders = server.Headers
        };
        if (server.CredentialProvider?.StartsWith("jarvis-mcp-", StringComparison.Ordinal) != true)
            return new HttpClientTransport(options, loggerFactory);

        var expectedHost = endpoint.IdnHost;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!context.DnsEndPoint.Host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException("The MCP server attempted to connect to an unapproved host.");
                var addresses = await McpServerEndpointValidator.ResolvePublicAddressesAsync(expectedHost, cancellationToken);
                Exception? lastFailure = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        if (exception is OperationCanceledException) throw;
                        lastFailure = exception;
                    }
                }
                throw new HttpRequestException("Could not connect to a public IP address for the MCP server.", lastFailure);
            }
        };
        var httpClient = new HttpClient(handler, disposeHandler: true);
        return new HttpClientTransport(options, httpClient, loggerFactory, ownsHttpClient: true);
    }

    private static bool IsEnvironmentName(string name) =>
        name.Length is > 0 and <= 128 &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool IsHeaderName(string name) =>
        name.Length is > 0 and <= 128 && name.All(character =>
            char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));

    private static bool IsHeaderPrefix(string prefix) =>
        prefix.Length is > 0 and <= 32 && char.IsAsciiLetter(prefix[0]) &&
        prefix.All(character => char.IsAsciiLetterOrDigit(character) || "+-.".Contains(character));

    private static void ValidateApprovalPolicy(IReadOnlySet<string> allowed,
        IReadOnlySet<string> autoApproved, string serverName)
    {
        if (!autoApproved.IsSubsetOf(allowed))
            throw new InvalidOperationException($"MCP server '{serverName}' marks a tool auto-approved that is not in its allowed-tools list.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
    }
}

public sealed class McpAuthorizationRequiredException(string endpoint, Exception inner)
    : InvalidOperationException("This MCP server requires authorization before Jarvis can use it.", inner)
{
    public string Endpoint { get; } = endpoint;
}

public sealed record McpServerConnectionStatus(string Name, string State, int ToolCount, string? Issue,
    string? Id = null, bool Enabled = true, IReadOnlyList<string>? Tools = null, bool HostManaged = false,
    string? Transport = null, string? CredentialProvider = null, IReadOnlyList<string>? ConfiguredTools = null,
    bool? HasCredentials = null);

public static class McpServerConfiguration
{
    public static McpServerOptions[] Read(IConfiguration configuration) =>
        configuration.GetSection("Mcp:Servers").GetChildren()
            .Select(section => section.Get<McpServerOptions>())
            .Where(server => server is not null)
            .Cast<McpServerOptions>()
            .ToArray();

    public static McpServerOptions? Find(IConfiguration configuration, string name) =>
        Read(configuration).FirstOrDefault(server =>
            server.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed class McpServerOptions
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string[] Arguments { get; set; } = [];
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CredentialProvider { get; set; }
    public Dictionary<string, string> CredentialEnvironmentVariables { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CredentialHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CredentialHeaderPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] AllowedTools { get; set; } = [];
    public string[] AutoApprovedTools { get; set; } = [];
    public bool OwnerNarrowed { get; set; }
    public string? WorkingDirectory { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 30;
}
