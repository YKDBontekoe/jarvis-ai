using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Jarvis.Mcp;

/// <summary>Where the MCP runner listens, read from McpRunner:Url and McpRunner:Token.</summary>
public sealed record McpRunnerOptions(Uri Url, string Token)
{
    public static McpRunnerOptions? From(IConfiguration configuration)
    {
        var url = configuration["McpRunner:Url"];
        var token = configuration["McpRunner:Token"];
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
            throw new InvalidOperationException("McpRunner:Url must be a ws:// or wss:// URL.");
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new InvalidOperationException("McpRunner:Token must be at least 32 characters when McpRunner:Url is set.");
        return new McpRunnerOptions(uri, token);
    }
}

/// <summary>What the runner starts for one connection. Sent once, as the first WebSocket message.</summary>
public sealed record McpRunnerLaunch(string Command, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpRunnerLaunch))]
public sealed partial class McpRunnerJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>
/// Runs a stdio MCP server in the separate MCP runner instead of inside this process. The runner starts the
/// package for this connection only and stops it when the connection closes, so the lifetime matches a local
/// stdio server. MCP messages then flow over the WebSocket exactly as they would over stdin and stdout.
/// </summary>
public sealed class McpRunnerClientTransport(McpRunnerOptions options, McpRunnerLaunch launch, string name,
    ILoggerFactory loggerFactory) : IClientTransport
{
    public string Name => name;

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + options.Token);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(options.Url, cancellationToken);
            var payload = JsonSerializer.SerializeToUtf8Bytes(launch, McpRunnerJsonContext.Default.McpRunnerLaunch);
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            var stream = WebSocketStream.Create(socket, WebSocketMessageType.Binary, ownsWebSocket: true);
            return await new StreamClientTransport(stream, stream, loggerFactory).ConnectAsync(cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
