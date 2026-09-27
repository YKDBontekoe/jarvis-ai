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

public sealed class McpToolHost(IConfiguration configuration, ILogger<McpToolHost> logger,
    ILoggerFactory loggerFactory,
    ICurrentUser currentUser, IIntegrationCredentialStore credentialStore,
    IUserMcpServerRegistry userMcpServers) : IAsyncDisposable
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
        await using var client = await McpClient.CreateAsync(
            CreateTransport(server, loggerFactory), cancellationToken: cancellationToken);
        var availableTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return availableTools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        var configuredServers = configuration.GetSection("Mcp:Servers").GetChildren()
            .Select(section => section.Get<McpServerOptions>())
            .Where(server => server is not null)
            .Cast<McpServerOptions>()
            .ToList();
        foreach (var userServer in await userMcpServers.ListAsync(currentUser.OwnerId, cancellationToken))
        {
            var secrets = await credentialStore.GetSecretsAsync(currentUser.OwnerId, userServer.Id, cancellationToken);
            var server = new McpServerOptions
            {
                Name = userServer.Name,
                Transport = "streamableHttp",
                Endpoint = userServer.Endpoint,
                AllowedTools = userServer.AllowedTools.ToArray(),
                CredentialProvider = userServer.Id,
                ConnectionTimeoutSeconds = 30
            };
            if (secrets?.TryGetValue("token", out _) == true)
            {
                server.CredentialHeaders["Authorization"] = "token";
                server.CredentialHeaderPrefixes["Authorization"] = "Bearer";
            }
            configuredServers.Add(server);
        }
        var servers = configuredServers.ToArray();
        var tools = new List<AITool>();
        var toolNames = new HashSet<string>(StringComparer.Ordinal);
        var statuses = new List<McpServerConnectionStatus>();
        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                logger.LogWarning("Skipping an MCP server with no configured name.");
                statuses.Add(new McpServerConnectionStatus("(unnamed)", "unavailable", 0, "invalid_configuration"));
                continue;
            }
            if (server.AllowedTools.Length == 0)
            {
                logger.LogWarning("Skipping MCP server {ServerName}: no tools are explicitly allowlisted.", server.Name);
                statuses.Add(new McpServerConnectionStatus(server.Name, "disabled", 0, "no_tools_allowlisted"));
                continue;
            }

            IAsyncDisposable? client = null;
            try
            {
                var credentialResolution = await ResolveCredentialsAsync(server, cancellationToken);
                if (!credentialResolution.Ready)
                {
                    statuses.Add(new McpServerConnectionStatus(server.Name, "needs_credentials", 0, "credentials_required"));
                    continue;
                }
                var serverSecrets = credentialResolution.Secrets;
                if (server.CredentialProvider?.StartsWith("jarvis-mcp-", StringComparison.Ordinal) == true)
                    await McpServerEndpointValidator.ValidateAsync(server.Endpoint, cancellationToken);

                var connectedClient = await McpClient.CreateAsync(CreateTransport(server, loggerFactory), cancellationToken: cancellationToken);
                client = connectedClient;
                var availableTools = await connectedClient.ListToolsAsync(cancellationToken: cancellationToken);
                var allowAllTools = server.AllowedTools.Contains("*", StringComparer.Ordinal);
                var availableNames = availableTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
                var allowed = allowAllTools
                    ? availableNames
                    : server.AllowedTools.ToHashSet(StringComparer.Ordinal);
                var autoApproved = server.AutoApprovedTools.ToHashSet(StringComparer.Ordinal);
                ValidateApprovalPolicy(allowed, autoApproved, server.Name);
                var transportSecrets = server.Headers.Values
                    .Concat(server.EnvironmentVariables.Values)
                    .Concat(serverSecrets?.Values ?? [])
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var selected = availableTools.Where(tool => allowed.Contains(tool.Name)).Select(tool =>
                {
                    if (tool is not AIFunction function)
                        throw new InvalidOperationException($"MCP tool '{tool.Name}' cannot be wrapped in an approval requirement.");
                    var protectedFunction = transportSecrets.Length == 0
                        ? function
                        : new SecretRedactingAIFunction(function, transportSecrets);
                    return autoApproved.Contains(tool.Name)
                        ? (AITool)protectedFunction
                        : new ApprovalRequiredAIFunction(protectedFunction);
                }).ToArray();
                var missing = allowAllTools
                    ? []
                    : allowed.Except(availableNames, StringComparer.Ordinal).ToArray();
                if (missing.Length != 0)
                    throw new InvalidOperationException($"MCP server '{server.Name}' does not provide allowlisted tools: {string.Join(", ", missing)}.");

                var duplicateTools = selected.Where(tool => toolNames.Contains(tool.Name)).Select(tool => tool.Name).ToArray();
                if (duplicateTools.Length != 0)
                    throw new InvalidOperationException($"MCP server '{server.Name}' provides tool names already provided by another configured server.");

                tools.AddRange(selected);
                foreach (var tool in selected) toolNames.Add(tool.Name);
                _clients.Add(connectedClient);
                client = null;
                statuses.Add(new McpServerConnectionStatus(server.Name, "connected", selected.Length, null));
                logger.LogInformation("Connected MCP server {ServerName}; enabled {ToolCount} allowlisted tools.", server.Name, selected.Length);
            }
            catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled)
            {
                if (client is not null) await client.DisposeAsync();
                throw canceled;
            }
            catch (Exception exception)
            {
                if (client is not null)
                {
                    try { await client.DisposeAsync(); }
                    catch { /* Keep the owning agent run available when an optional server fails. */ }
                }
                var issue = exception is ArgumentException or InvalidOperationException
                    ? "invalid_configuration"
                    : "server_unavailable";
                statuses.Add(new McpServerConnectionStatus(server.Name, "unavailable", 0, issue));
                logger.LogWarning("Skipping MCP server {ServerName} after initialization failure ({FailureType}).",
                    server.Name, exception.GetType().Name);
            }
        }

        _tools = tools;
        _statuses = statuses;
        _initialized = true;
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

public sealed record McpServerConnectionStatus(string Name, string State, int ToolCount, string? Issue);

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
    public string? WorkingDirectory { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 30;
}
