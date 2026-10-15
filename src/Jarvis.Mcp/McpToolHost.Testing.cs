using System.Net;
using System.Text.Json;
using Jarvis.Application;
using Jarvis.Application.Integrations;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Jarvis.Mcp;

/// <summary>
/// The result of connecting to one server on request. <see cref="ErrorKind"/> is unauthorized, missing_key,
/// unreachable, crashed, timeout, runner_required, not_activated, paused or unavailable; it never carries secret
/// values or response bodies.
/// </summary>
public sealed record McpConnectionTestResult(bool Ok, string State, int ToolCount, IReadOnlyList<string> Tools,
    string? ErrorKind, string? Message);

public sealed partial class McpToolHost
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    // A first run of a self-built server downloads Python packages, so it gets longer than a registry check.
    private static readonly TimeSpan ScriptTestTimeout = TimeSpan.FromSeconds(150);

    /// <summary>Connects to one of the owner's servers (or a host server) once and reports whether it works.</summary>
    public async Task<McpConnectionTestResult> TestServerAsync(string serverKey, CancellationToken cancellationToken)
    {
        var resolved = await ResolveForUseAsync(serverKey, allowPaused: true, cancellationToken);
        if (resolved.Error is not null)
            return new McpConnectionTestResult(false, "unavailable", 0, [], "unavailable", resolved.Error);
        var server = resolved.Server!;
        if (CustomScriptIssue(server) is { } issue)
            return new McpConnectionTestResult(false, "unavailable", 0, [], issue, ExplainIssue(issue));
        if (server.MissingRequiredSecrets)
            return new McpConnectionTestResult(false, "needs_credentials", 0, [], "missing_key",
                "Add the app's keys first.");
        var credential = await ResolveCredentialsAsync(server, cancellationToken);
        if (!credential.Ready)
            return server.SignInExpired
                ? new McpConnectionTestResult(false, "needs_sign_in", 0, [], "unauthorized",
                    "The sign-in expired. Sign in again.")
                : new McpConnectionTestResult(false, "needs_credentials", 0, [], "missing_key",
                    "Add the app's keys first.");
        if (IntegrationCredentialProviders.IsUserMcpServerId(server.CredentialProvider) &&
            server.Transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase))
            await McpServerEndpointValidator.ValidateAsync(server.Endpoint, cancellationToken);

        var transport = CreateTransport(server, loggerFactory);
        var timeout = server.Script is null ? TestTimeout : ScriptTestTimeout;
        var (tools, _, failure) = await ConnectAndListAsync(transport, timeout, null, null, cancellationToken);
        if (failure is not null)
            return new McpConnectionTestResult(false, failure.Value.Kind == "unauthorized" ? "needs_sign_in" : "unavailable",
                0, [], failure.Value.Kind, failure.Value.Message);
        var allowed = McpToolSelection.AllowsAll(server.AllowedTools)
            ? tools
            : tools.Where(tool => server.AllowedTools.Contains(tool, StringComparer.Ordinal)).ToArray();
        var state = resolved.Enabled ? "connected" : "paused";
        return new McpConnectionTestResult(true, state, allowed.Length, allowed, resolved.Enabled ? null : "paused", null);
    }

    /// <summary>
    /// Runs the draft of a self-built server in the MCP runner, lists its tools and optionally calls one. The
    /// result, with the server's keys removed, is saved on the server so the app and Jarvis can show it.
    /// </summary>
    public async Task<CustomMcpTestResult> TestCustomServerAsync(Guid customId, string? toolName,
        string? argumentsJson, CancellationToken cancellationToken)
    {
        var detail = await customServers.GetAsync(currentUser.OwnerId, customId, cancellationToken)
                     ?? throw new ArgumentException("That self-built app does not exist.");
        var sha = detail.Server.DraftSha256;
        CustomMcpTestResult Fail(string error, string? diagnostics = null) =>
            new(false, sha, [], error, diagnostics, DateTimeOffset.UtcNow);

        var result = await RunCustomTestAsync(detail, toolName, argumentsJson, Fail, cancellationToken);
        await customServers.RecordTestAsync(currentUser.OwnerId, customId, result, cancellationToken);
        return result;
    }

    private async Task<CustomMcpTestResult> RunCustomTestAsync(CustomMcpServerDetail detail, string? toolName,
        string? argumentsJson, Func<string, string?, CustomMcpTestResult> fail, CancellationToken cancellationToken)
    {
        var runner = McpRunnerOptions.From(configuration);
        if (runner is null) return fail(ExplainIssue("runner_required"), null);
        var check = CustomMcpScriptValidator.Validate(detail.DraftSource);
        if (!check.Ok) return fail(string.Join(" ", check.Errors), null);
        Dictionary<string, object?>? arguments = null;
        if (!string.IsNullOrWhiteSpace(toolName))
        {
            try { arguments = ReadArguments(argumentsJson); }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                return fail("Tool arguments must be a JSON object.", null);
            }
        }

        var registered = (await userMcpServers.ListAsync(currentUser.OwnerId, cancellationToken))
            .FirstOrDefault(server => server.Id == detail.Server.RegistryServerId);
        var options = registered is null
            ? new McpServerOptions { Name = detail.Server.Name, Transport = "stdio" }
            : await CreateUserServerOptionsAsync(registered, cancellationToken);
        var credential = await ResolveCredentialsAsync(options, cancellationToken);
        var secrets = SecretValues(credential.Secrets, options);
        var transport = new McpRunnerClientTransport(runner,
            new McpRunnerLaunch("uv", [], RunnerEnvironment(options),
                new McpRunnerScript("python", check.Source, check.Sha256)), detail.Server.Name, loggerFactory);

        var (tools, toolResult, failure) = await ConnectAndListAsync(transport, ScriptTestTimeout, toolName,
            arguments, cancellationToken, secrets);
        var diagnostics = transport.Diagnostics?.StderrTail is { Length: > 0 } tail
            ? McpSecretRedactor.Bound(McpSecretRedactor.Redact(tail, secrets), 4000)
            : null;
        if (failure is not null)
        {
            var exit = transport.Diagnostics?.ExitCode is { } code ? $" The server stopped with exit code {code}." : "";
            return fail(failure.Value.Message + exit, diagnostics);
        }
        return new CustomMcpTestResult(true, check.Sha256, tools, null, diagnostics, DateTimeOffset.UtcNow,
            toolResult);
    }

    private async Task<(string[] Tools, string? ToolResult, (string Kind, string Message)? Failure)>
        ConnectAndListAsync(IClientTransport transport, TimeSpan timeout, string? toolName,
            Dictionary<string, object?>? arguments, CancellationToken cancellationToken, string[]? secrets = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: limit.Token);
            var tools = (await client.ListToolsAsync(cancellationToken: limit.Token))
                .Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                if (!tools.Contains(toolName, StringComparer.Ordinal))
                    return (tools, null, ("missing_tool", $"The server has no tool named '{toolName}'."));
                var called = await client.CallToolAsync(toolName, arguments, cancellationToken: limit.Token);
                var text = string.Join("\n", called.Content.OfType<TextContentBlock>().Select(block => block.Text));
                var toolResult = McpSecretRedactor.Bound(McpSecretRedactor.Redact(
                    (called.IsError == true ? "Error: " : "") + text, secrets), 2000);
                return (tools, toolResult, null);
            }
            return (tools, null, null);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested &&
                                          CancellationExceptions.Unwrap(exception) is { } canceled)
        {
            throw canceled;
        }
        catch (Exception exception)
        {
            var kind = ErrorKind(exception, transport);
            logger.LogInformation("MCP connection test failed ({FailureType}, {ErrorKind}).",
                exception.GetType().Name, kind);
            return ([], null, (kind, ExplainIssue(kind)));
        }
    }

    /// <summary>Sorts a connection failure into a kind the app can explain and offer a fix for.</summary>
    internal static string ErrorKind(Exception exception, IClientTransport? transport = null)
    {
        if (McpAuthorization.IsAuthorizationFailure(exception)) return "unauthorized";
        if (transport is McpRunnerClientTransport { Diagnostics: not null }) return "crashed";
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }:
                    return "unauthorized";
                case OperationCanceledException or TimeoutException:
                    return "timeout";
                case HttpRequestException or System.Net.Sockets.SocketException or
                    System.Net.WebSockets.WebSocketException:
                    return "unreachable";
                case IOException:
                    return "crashed";
            }
        }
        return exception is InvalidOperationException ? "unavailable" : "unreachable";
    }

    internal static string ExplainIssue(string kind) => kind switch
    {
        "unauthorized" => "The app rejected the key or sign-in. Replace the key or sign in again.",
        "missing_key" => "Add the app's keys first.",
        "timeout" => "The app did not answer in time.",
        "unreachable" => "Jarvis could not reach the app.",
        "crashed" => "The app stopped while starting.",
        "runner_required" => "Self-built apps run only in the isolated MCP runner, which is not configured on this server.",
        "not_activated" => "Approve the app's code first.",
        "missing_tool" => "That tool does not exist on this app.",
        _ => "The app is not available right now."
    };
}
