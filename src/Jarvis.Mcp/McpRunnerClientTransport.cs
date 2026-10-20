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

/// <summary>
/// What the runner starts for one connection. Sent once, as the first WebSocket message. A launch with
/// <see cref="Script"/> runs a self-built server's Python source with uv instead of a registry package;
/// <see cref="State"/> is what that server last saved, handed back to it as the file named by JARVIS_STATE_FILE.
/// </summary>
public sealed record McpRunnerLaunch(string Command, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment, McpRunnerScript? Script = null, string? State = null);

/// <summary>A self-built server's approved source. The runner checks the hash and the script rules again.</summary>
public sealed record McpRunnerScript(string Language, string Source, string Sha256);

/// <summary>
/// Sent by the runner as a text message when a self-built server stops on its own, so Jarvis can show why.
/// The stderr tail has the server's key values removed.
/// </summary>
public sealed record McpRunnerDiagnostics(string Type, int? ExitCode, string? StderrTail);

/// <summary>
/// Sent by the runner as a text message, ahead of the MCP reply that follows it, when a self-built server changed
/// its state file. An empty <see cref="State"/> means the server cleared it.
/// </summary>
public sealed record McpRunnerStateUpdate(string Type, string State);

/// <summary>The limits of a self-built server's saved state, which Jarvis keeps encrypted like its other keys.</summary>
public static class McpRunnerState
{
    public const string FileEnvironmentVariable = "JARVIS_STATE_FILE";

    /// <summary>The credential store refuses values over 16,384 characters.</summary>
    public const int MaxLength = 16_000;
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpRunnerLaunch))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpRunnerDiagnostics))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpRunnerStateUpdate))]
public sealed partial class McpRunnerJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>
/// Runs a stdio MCP server in the separate MCP runner instead of inside this process. The runner starts the
/// package for this connection only and stops it when the connection closes, so the lifetime matches a local
/// stdio server. MCP messages then flow over the WebSocket exactly as they would over stdin and stdout.
/// </summary>
public sealed class McpRunnerClientTransport(McpRunnerOptions options, McpRunnerLaunch launch, string name,
    ILoggerFactory loggerFactory, Action<string>? onState = null) : IClientTransport
{
    public string Name => name;

    /// <summary>What the runner reported when the server stopped on its own, if it did.</summary>
    public McpRunnerDiagnostics? Diagnostics { get; private set; }

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
            var stream = new RunnerSocketStream(socket, diagnostics => Diagnostics = diagnostics, onState);
            return await new StreamClientTransport(stream, stream, loggerFactory).ConnectAsync(cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// MCP bytes travel as binary WebSocket messages. A text message from the runner is a diagnostics report or a
/// state update, kept aside instead of being passed to the MCP client as protocol data.
/// </summary>
internal sealed class RunnerSocketStream(WebSocket socket, Action<McpRunnerDiagnostics> onDiagnostics,
    Action<string>? onState = null) : Stream
{
    private const int MaxDiagnosticsBytes = 16 * 1024;
    // Up to 16,000 characters of state, each at most three UTF-8 bytes, plus the JSON around it.
    private const int MaxStateBytes = 64 * 1024;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseSent)) return 0;
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return 0;
            if (result.MessageType == WebSocketMessageType.Binary) return result.Count;
            await ReadTextMessageAsync(buffer[..result.Count].ToArray(), result.EndOfMessage, cancellationToken);
        }
    }

    private async Task ReadTextMessageAsync(byte[] first, bool complete, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        message.Write(first);
        var overflow = first.Length > MaxStateBytes;
        var chunk = new byte[4096];
        while (!complete)
        {
            var result = await socket.ReceiveAsync(chunk, cancellationToken);
            if (message.Length + result.Count <= MaxStateBytes) message.Write(chunk, 0, result.Count);
            else overflow = true;
            complete = result.EndOfMessage;
        }
        if (overflow) return;
        try
        {
            using var document = JsonDocument.Parse(message.ToArray());
            var type = document.RootElement.TryGetProperty("type", out var value) ? value.GetString() : null;
            if (type == "diagnostics" && message.Length <= MaxDiagnosticsBytes)
            {
                var diagnostics = document.RootElement.Deserialize(McpRunnerJsonContext.Default.McpRunnerDiagnostics);
                if (diagnostics is not null) onDiagnostics(diagnostics);
            }
            else if (type == "state" && onState is not null)
            {
                var update = document.RootElement.Deserialize(McpRunnerJsonContext.Default.McpRunnerStateUpdate);
                if (update is { State.Length: <= McpRunnerState.MaxLength }) onState(update.State);
            }
        }
        catch (JsonException) { }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        await socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) socket.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException) { }
        }
        socket.Dispose();
        await base.DisposeAsync();
    }
}
