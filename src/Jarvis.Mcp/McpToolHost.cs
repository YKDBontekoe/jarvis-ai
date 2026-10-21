using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Jarvis.Application;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Jarvis.Mcp;

public sealed partial class McpToolHost(IConfiguration configuration, ILogger<McpToolHost> logger,
    ILoggerFactory loggerFactory,
    ICurrentUser currentUser, IIntegrationCredentialStore credentialStore,
    IUserMcpServerRegistry userMcpServers, IOwnerMcpPolicyStore ownerPolicy,
    ICustomMcpServerStore customServers) : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _pendingState =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private IReadOnlyList<AITool> _tools = [];
    private IReadOnlyList<McpServerConnectionStatus> _statuses = [];
    private bool _initialized;

    public IReadOnlyList<AITool> Tools => _tools;

    private readonly HashSet<string> _readOnlyTools = new(StringComparer.Ordinal);

    /// <summary>
    /// True for an integration tool whose server declared it read-only (and not destructive). It is a claim by the
    /// server, so the approval policy only acts on it when the owner allows that.
    /// </summary>
    public bool IsReadOnlyTool(string toolName) => _readOnlyTools.Contains(toolName);

    /// <summary>Only an explicit read-only hint counts; the MCP default is "not read-only".</summary>
    public static bool DeclaresReadOnly(ModelContextProtocol.Protocol.ToolAnnotations? annotations) =>
        annotations is { ReadOnlyHint: true } && annotations.DestructiveHint != true;
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
                    userServer.Id, false, userServer.AllowedTools)
                {
                    SelfBuilt = userServer.Command == McpStdioCommandValidator.CustomScriptCommand
                });
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

        foreach (var server in servers)
        {
            IAsyncDisposable? client = null;
            try
            {
                if (CustomScriptIssue(server) is { } scriptIssue)
                {
                    statuses.Add(await BuildStatusAsync(server, "unavailable", 0, scriptIssue, true, null,
                        cancellationToken));
                    continue;
                }
                var credentialResolution = server.MissingRequiredSecrets
                    ? (Ready: false, Secrets: null)
                    : await ResolveCredentialsAsync(server, cancellationToken);
                if (!credentialResolution.Ready)
                {
                    statuses.Add(server.SignInExpired
                        ? await BuildStatusAsync(server, "needs_sign_in", 0, "sign_in_expired", true, null,
                            cancellationToken)
                        : await BuildStatusAsync(server, "needs_credentials", 0, "credentials_required", true,
                            null, cancellationToken));
                    continue;
                }
                var serverSecrets = credentialResolution.Secrets;
                if (IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider))
                {
                    if (server.Transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase))
                        await McpServerEndpointValidator.ValidateAsync(server.Endpoint, cancellationToken);
                }

                // A server that hangs while starting (for example an npx package that cannot download) must
                // cost one status entry, not the whole agent turn, so bound the connect and tool listing.
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(server.ConnectionTimeoutSeconds, 1, 300)));
                var connectedClient = await McpClient.CreateAsync(CreateTransport(server, loggerFactory),
                    cancellationToken: connectTimeout.Token);
                client = connectedClient;
                var availableTools = await connectedClient.ListToolsAsync(cancellationToken: connectTimeout.Token);
                var allowAllTools = McpToolSelection.AllowsAll(server.AllowedTools);
                var availableNames = availableTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
                if (allowAllTools && availableNames.Count > McpToolSelection.MaxTools)
                {
                    await CloseQuietlyAsync(connectedClient);
                    client = null;
                    statuses.Add(await BuildStatusAsync(server, "unavailable", 0, "too_many_tools", true, null,
                        cancellationToken));
                    logger.LogWarning("Skipping MCP server {ServerName}: it exposes {ToolCount} tools, above the {MaxTools} tool limit.",
                        server.Name, availableNames.Count, McpToolSelection.MaxTools);
                    continue;
                }
                var allowed = allowAllTools
                    ? availableNames
                    : server.AllowedTools.ToHashSet(StringComparer.Ordinal);
                if (server.OwnerNarrowed)
                {
                    allowed.IntersectWith(availableNames);
                    if (allowed.Count == 0)
                    {
                        await CloseQuietlyAsync(connectedClient);
                        client = null;
                        statuses.Add(await BuildStatusAsync(server, "disabled", 0, "no_matching_tools", true, [],
                            cancellationToken));
                        continue;
                    }
                }
                var autoApproved = server.AutoApprovedTools.ToHashSet(StringComparer.Ordinal);
                if (server.OwnerNarrowed) autoApproved.IntersectWith(allowed);
                ValidateApprovalPolicy(allowed, autoApproved, server.Name);
                var transportSecrets = SecretValues(serverSecrets, server);
                var serverKey = UserServerId(server) ?? server.Name;
                var selected = availableTools.Where(tool => allowed.Contains(tool.Name)).Select(tool =>
                {
                    if (tool is not AIFunction function)
                        throw new InvalidOperationException($"MCP tool '{tool.Name}' cannot be wrapped in an approval requirement.");
                    AIFunction protectedFunction = transportSecrets.Length == 0
                        ? function
                        : new SecretRedactingAIFunction(function, transportSecrets);
                    protectedFunction = new GuardedMcpTool(protectedFunction, this, serverKey, tool.Name);
                    if (autoApproved.Contains(tool.Name)) return (AITool)protectedFunction;
                    if (tool is McpClientTool mcpTool && DeclaresReadOnly(mcpTool.ProtocolTool.Annotations))
                        _readOnlyTools.Add(tool.Name);
                    return new ApprovalRequiredAIFunction(protectedFunction);
                }).ToArray();
                var missing = allowAllTools || server.OwnerNarrowed
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
                Remember(server, new LiveSession(connectedClient, transportSecrets));
                client = null;
                statuses.Add(await BuildStatusAsync(server, "connected", selected.Length, null, true,
                    selected.Select(tool => tool.Name).ToArray(), cancellationToken) with
                {
                    ReadOnlyTools = selected.Select(tool => tool.Name).Where(_readOnlyTools.Contains).ToArray()
                });
                logger.LogInformation("Connected MCP server {ServerName}; enabled {ToolCount} allowlisted tools.", server.Name, selected.Length);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested &&
                                              CancellationExceptions.Unwrap(exception) is { } canceled)
            {
                if (client is not null) await client.DisposeAsync();
                throw canceled;
            }
            catch (Exception exception)
            {
                if (client is not null) await CloseQuietlyAsync(client);
                var issue = exception is ArgumentException or InvalidOperationException
                    ? "invalid_configuration"
                    : CancellationExceptions.Unwrap(exception) is not null ? "connection_timed_out"
                    : "server_unavailable";
                statuses.Add(await BuildStatusAsync(server, "unavailable", 0, issue, true, null, cancellationToken));
                logger.LogWarning("Skipping MCP server {ServerName} after initialization failure ({FailureType}).",
                    server.Name, exception.GetType().Name);
            }
        }

        _tools = tools;
        _statuses = statuses;
        _initialized = true;
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
        if (server.Command == McpStdioCommandValidator.CustomScriptCommand &&
            CustomMcpNames.IdFromArguments(server.Arguments) is { } customId)
        {
            server.CustomServerId = customId;
            if (await customServers.GetActiveSourceAsync(currentUser.OwnerId, customId, cancellationToken) is { } active)
                server.Script = new McpRunnerScript("python", active.Source, active.Sha256);
            if (secrets?.GetValueOrDefault(IntegrationCredentialProviders.UserMcpStateSecret) is { Length: > 0 } state)
                server.ScriptState = state;
        }
        foreach (var binding in userServer.SecretBindings ?? [])
        {
            if (secrets?.ContainsKey(binding.SecretName) != true)
            {
                if (binding.Required) server.MissingRequiredSecrets = true;
                continue;
            }
            if (binding.Target == McpSecretBindings.EnvironmentTarget &&
                McpSecretBindings.IsAllowedEnvironmentName(binding.Key))
                server.CredentialEnvironmentVariables[binding.Key] = binding.SecretName;
            else if (binding.Target == McpSecretBindings.HeaderTarget &&
                     McpSecretBindings.IsAllowedHeaderName(binding.Key))
            {
                server.CredentialHeaders[binding.Key] = binding.SecretName;
                if (binding.Prefix is { } prefix) server.CredentialHeaderPrefixes[binding.Key] = prefix;
            }
        }
        // An OAuth or pasted access token applies unless a declared secret already fills Authorization.
        if (transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) &&
            secrets?.ContainsKey("token") == true && !server.CredentialHeaders.ContainsKey("Authorization"))
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
        if (McpOAuthTokens.NeedsRefresh(secrets, DateTimeOffset.UtcNow))
        {
            var renewed = await RenewSignInAsync(provider, secrets, cancellationToken);
            if (renewed is null)
            {
                server.SignInExpired = true;
                return (false, null);
            }
            secrets = renewed;
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

    private static readonly HttpClient OAuthHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>Renews an expiring OAuth sign-in and returns the updated secrets, or null when it cannot.</summary>
    private async Task<IReadOnlyDictionary<string, string>?> RenewSignInAsync(string provider,
        IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        try
        {
            var tokens = await McpOAuthTokens.RefreshAsync(OAuthHttp, secrets, DateTimeOffset.UtcNow, cancellationToken);
            await McpOAuthTokens.SaveAsync(credentialStore, currentUser.OwnerId, provider, tokens,
                secrets.GetValueOrDefault(McpOAuthTokens.TokenEndpointSecret),
                secrets.GetValueOrDefault(McpOAuthTokens.ClientIdSecret),
                secrets.GetValueOrDefault(McpOAuthTokens.ResourceSecret), cancellationToken);
            logger.LogInformation("Renewed the OAuth sign-in for MCP provider {Provider}.", provider);
            return await credentialStore.GetSecretsAsync(currentUser.OwnerId, provider, cancellationToken) ?? secrets;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is InvalidOperationException or HttpRequestException or
                                              JsonException or TaskCanceledException)
        {
            logger.LogWarning("Could not renew the OAuth sign-in for MCP provider {Provider} ({FailureType}).",
                provider, exception.GetType().Name);
            return null;
        }
    }

    private IClientTransport CreateTransport(McpServerOptions server, ILoggerFactory loggerFactory)
    {
        if (server.ConnectionTimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException($"MCP server '{server.Name}' connection timeout must be between 1 and 300 seconds.");

        if (server.Command == McpStdioCommandValidator.CustomScriptCommand)
            return CreateCustomScriptTransport(server, loggerFactory);

        return server.Transport.Trim().ToLowerInvariant() switch
        {
            // Owner-added connectors run in the separate MCP runner when one is configured, so they never see
            // this process's environment, files, or other owners' secrets. Operator host binaries stay local.
            "stdio" when IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) &&
                         McpRunnerOptions.From(configuration) is { } runner =>
                new McpRunnerClientTransport(runner,
                    new McpRunnerLaunch(server.Command, server.Arguments, RunnerEnvironment(server)),
                    server.Name, loggerFactory),
            "stdio" => CreateStdioTransport(server),
            "streamablehttp" => CreateHttpTransport(server, loggerFactory),
            var transport => throw new InvalidOperationException(
                $"MCP server '{server.Name}' has unsupported transport '{transport}'. Use 'stdio' or 'streamableHttp'.")
        };
    }

    /// <summary>Why a self-built server cannot start, or null when it can.</summary>
    private string? CustomScriptIssue(McpServerOptions server)
    {
        if (server.Command != McpStdioCommandValidator.CustomScriptCommand) return null;
        if (!IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) || server.CustomServerId is null)
            return "invalid_configuration";
        if (McpRunnerOptions.From(configuration) is null) return "runner_required";
        return server.Script is null ? "not_activated" : null;
    }

    /// <summary>Self-built servers only ever run in the MCP runner, never inside this process.</summary>
    private McpRunnerClientTransport CreateCustomScriptTransport(McpServerOptions server, ILoggerFactory loggerFactory)
    {
        if (CustomScriptIssue(server) is { } issue)
            throw new InvalidOperationException(issue switch
            {
                "runner_required" => "Self-built apps need the MCP runner (McpRunner:Url).",
                "not_activated" => "This self-built app has no approved code yet.",
                _ => "This self-built app is not configured correctly."
            });
        var provider = server.CredentialProvider!;
        return new McpRunnerClientTransport(McpRunnerOptions.From(configuration)!,
            new McpRunnerLaunch("uv", [], RunnerEnvironment(server), server.Script, server.ScriptState),
            server.Name, loggerFactory, state => RememberScriptState(provider, state));
    }

    internal void RememberScriptState(string provider, string state) => _pendingState[provider] = state;

    /// <summary>
    /// Saves what a self-built server last wrote to its state file (a login session, say) next to its keys, so the
    /// next run starts logged in. The runner reports a change just before the tool reply that follows it, so this
    /// runs after each call and when the host is disposed. Saves go one at a time because the store shares one
    /// database connection.
    /// </summary>
    internal async Task PersistScriptStateAsync(string provider, CancellationToken cancellationToken)
    {
        if (!_pendingState.TryRemove(provider, out var state)) return;
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            if (state.Length == 0)
                await credentialStore.DeleteSecretAsync(currentUser.OwnerId, provider,
                    IntegrationCredentialProviders.UserMcpStateSecret, cancellationToken);
            else
                await credentialStore.SaveSecretAsync(currentUser.OwnerId, provider,
                    IntegrationCredentialProviders.UserMcpStateSecret, state, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Kept for the next attempt (after the next call or when the host is disposed) unless the server has
            // reported something newer since.
            _pendingState.TryAdd(provider, state);
            logger.LogWarning("Could not save the state of MCP provider {Provider} ({FailureType}).",
                provider, exception.GetType().Name);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static Dictionary<string, string> RunnerEnvironment(McpServerOptions server) =>
        server.EnvironmentVariables
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

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
        foreach (var provider in _pendingState.Keys.ToArray())
            await PersistScriptStateAsync(provider, CancellationToken.None);
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
    bool? HasCredentials = null)
{
    /// <summary>Enabled tools the server marks read-only; they can run without a card when the owner allows it.</summary>
    public IReadOnlyList<string>? ReadOnlyTools { get; init; }

    /// <summary>True for an app Jarvis wrote for the owner.</summary>
    public bool SelfBuilt { get; init; }
}

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

    /// <summary>A secret the owner's server declares as required has not been provided yet.</summary>
    public bool MissingRequiredSecrets { get; set; }

    /// <summary>The OAuth sign-in expired and could not be renewed, so the owner must sign in again.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SignInExpired { get; set; }
    public string? WorkingDirectory { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>A self-built server's id and approved source. Never read from configuration.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? CustomServerId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public McpRunnerScript? Script { get; set; }

    /// <summary>What a self-built server saved on its last run. Never read from configuration.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ScriptState { get; set; }
}
