using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Jarvis.Application.Integrations;
using Jarvis.Application.Security;
using Jarvis.Mcp;

namespace Jarvis.Api.McpRunner;

/// <summary>
/// The MCP runner (<c>Jarvis.Api.dll mcp-runner</c>): starts owner-installed npm and PyPI connectors in their
/// own container, away from the API and worker, and bridges each one to a single authenticated WebSocket.
/// A connector gets a fresh temporary home, only the keys sent for it, and is stopped with its whole process
/// tree when the connection closes.
/// </summary>
internal static partial class McpRunnerHost
{
    // Room for a self-built server's source (at most 64 KB), its saved state (at most 16,000 characters) and its keys.
    private const int MaxLaunchBytes = 256 * 1024;
    internal const string StateFileName = "jarvis-state.json";
    private const int MaxStderrTailBytes = 4 * 1024;
    internal const string ScriptFileName = "server.py";
    internal static readonly string[] ScriptArguments =
        ["run", "--no-config", "--no-project", "--quiet", "--script", ScriptFileName];
    private const int MaxEnvironmentVariables = 16;
    private const int MaxEnvironmentValueLength = 8 * 1024;

    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.WebHost.UseUrls(builder.Configuration["McpRunner:ListenUrl"] ?? "http://0.0.0.0:8090");
        await Build(builder).RunAsync();
    }

    internal static WebApplication Build(WebApplicationBuilder builder)
    {
        var token = builder.Configuration["McpRunner:Token"];
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new InvalidOperationException("Set McpRunner:Token to at least 32 random characters.");
        var settings = RunnerSettings.From(builder.Configuration);
        var app = builder.Build();
        var slots = new SemaphoreSlim(settings.MaxProcesses);
        var logger = app.Logger;

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.Map("/run", async context =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) ||
                !SecretComparer.FixedTimeEquals(token, header["Bearer ".Length..]))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!await slots.WaitAsync(0))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await RunSessionAsync(socket, settings, logger, context.RequestAborted);
            }
            finally
            {
                slots.Release();
            }
        });
        return app;
    }

    internal static async Task RunSessionAsync(WebSocket socket, RunnerSettings settings, ILogger logger,
        CancellationToken cancellationToken)
    {
        McpRunnerLaunch launch;
        try
        {
            launch = await ReadLaunchAsync(socket, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            LogRejected(logger, exception.Message);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, exception.Message);
            return;
        }

        var workDirectory = Path.Combine(settings.WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        StateSync? stateSync = null;
        if (launch.Script is { } script)
        {
            await File.WriteAllTextAsync(Path.Combine(workDirectory, ScriptFileName), script.Source, cancellationToken);
            stateSync = new StateSync(Path.Combine(workDirectory, StateFileName), launch.State);
            await stateSync.InitializeAsync(cancellationToken);
        }
        using var process = new Process { StartInfo = StartInfo(launch, settings, workDirectory) };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("The connector did not start.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LogStartFailed(logger, launch.Command, exception.GetType().Name);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.InternalServerError, "The connector could not start.");
            TryDelete(workDirectory);
            return;
        }

        LogStarted(logger, launch.Command, PackageOf(launch));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(settings.MaxSession);
        await using var stream = WebSocketStream.Create(socket, WebSocketMessageType.Binary, ownsWebSocket: false);
        var toConnector = PumpAsync(stream, process.StandardInput.BaseStream, lifetime.Token);
        var fromConnector = PumpAsync(process.StandardOutput.BaseStream, stream, lifetime.Token,
            stateSync is null ? null : () => SendStateAsync(socket, stateSync, lifetime.Token));
        // Connector logs can contain whatever the package prints, including its own keys, so they are dropped.
        // A self-built server keeps a short tail, with its key values removed, so Jarvis can see why it stopped.
        var stderrTail = launch.Script is null ? null : new StderrTail(MaxStderrTailBytes);
        var drainErrors = stderrTail is null
            ? process.StandardError.BaseStream.CopyToAsync(Stream.Null, lifetime.Token)
            : stderrTail.ReadAllAsync(process.StandardError.BaseStream, lifetime.Token);
        var exitedOnItsOwn = false;
        try
        {
            var finished = await Task.WhenAny(toConnector, fromConnector, process.WaitForExitAsync(lifetime.Token));
            // The connector closed its output or exited, rather than Jarvis hanging up or the session timing out.
            exitedOnItsOwn = finished != toConnector && !lifetime.IsCancellationRequested;
        }
        catch (OperationCanceledException) { }
        finally
        {
            try
            {
                // Let the connector finish exiting and its last error lines arrive before reporting why it stopped.
                if (exitedOnItsOwn && stderrTail is not null)
                {
                    await Task.WhenAny(
                        Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), Observe(drainErrors)),
                        Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));
                    if (stateSync is not null) await SendStateAsync(socket, stateSync, CancellationToken.None);
                    // Sent while the socket is still open: cancelling the pending receive below aborts it.
                    await SendDiagnosticsAsync(socket, new McpRunnerDiagnostics("diagnostics",
                        process.HasExited ? process.ExitCode : null,
                        Redact(stderrTail.Text(), launch.Environment.Values)));
                }
            }
            catch (OperationCanceledException) { }
            await lifetime.CancelAsync();
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                else if (process.ExitCode != 0)
                    LogExitFailure(logger, launch.Command, PackageOf(launch), process.ExitCode);
            }
            catch (InvalidOperationException) { }
            await Task.WhenAll(Observe(toConnector), Observe(fromConnector), Observe(drainErrors));

            await CloseQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, "Connector stopped.");
            TryDelete(workDirectory);
            LogStopped(logger, launch.Command, PackageOf(launch));
        }
    }

    internal static async Task<McpRunnerLaunch> ReadLaunchAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxLaunchBytes];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length) throw new InvalidOperationException("The launch request is too large.");
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("Send the launch request as one text message first.");
            length += result.Count;
            if (result.EndOfMessage) break;
        }
        var launch = JsonSerializer.Deserialize(buffer.AsSpan(0, length), McpRunnerJsonContext.Default.McpRunnerLaunch)
                     ?? throw new InvalidOperationException("The launch request is empty.");
        return Validate(launch);
    }

    /// <summary>
    /// Only npx and uvx packages and self-built Python servers run here; operator host binaries stay in the API
    /// image.
    /// </summary>
    internal static McpRunnerLaunch Validate(McpRunnerLaunch launch)
    {
        if (launch.Script is not null) return ValidateScript(launch);
        if (launch.Command is not ("npx" or "uvx"))
            throw new ArgumentException("The runner starts npx and uvx connectors only.");
        var (command, arguments) = McpStdioCommandValidator.Normalize(launch.Command, launch.Arguments);
        return new McpRunnerLaunch(command, arguments, ValidateEnvironment(launch.Environment));
    }

    private static McpRunnerLaunch ValidateScript(McpRunnerLaunch launch)
    {
        var script = launch.Script!;
        if (launch.Command != "uv" || launch.Arguments.Count != 0 || script.Language != "python")
            throw new ArgumentException("A self-built server runs as a Python script with uv.");
        var check = CustomMcpScriptValidator.Validate(script.Source);
        if (!check.Ok) throw new ArgumentException("The server code breaks the script rules.");
        if (!string.Equals(check.Sha256, script.Sha256, StringComparison.Ordinal))
            throw new ArgumentException("The server code does not match its approved hash.");
        var environment = ValidateEnvironment(launch.Environment);
        var state = launch.State;
        if (state is not null && (state.Length > McpRunnerState.MaxLength || state.Contains('\0')))
            throw new ArgumentException("The saved state is not valid.");
        return new McpRunnerLaunch("uv", ScriptArguments, environment,
            new McpRunnerScript("python", check.Source, check.Sha256), string.IsNullOrEmpty(state) ? null : state);
    }

    private static IReadOnlyDictionary<string, string> ValidateEnvironment(IReadOnlyDictionary<string, string>? values)
    {
        var environment = values ?? new Dictionary<string, string>();
        if (environment.Count > MaxEnvironmentVariables)
            throw new ArgumentException("Too many environment variables.");
        foreach (var (name, value) in environment)
        {
            if (!McpSecretBindings.IsAllowedEnvironmentName(name))
                throw new ArgumentException($"The runner does not set {name}.");
            if (value is null || value.Length > MaxEnvironmentValueLength || value.Contains('\0'))
                throw new ArgumentException($"The value for {name} is not valid.");
        }
        return environment;
    }

    internal static string Redact(string text, IEnumerable<string> secrets) =>
        McpSecretRedactor.Redact(text, secrets.Where(secret => secret.Length >= 4));

    private static async Task SendStateAsync(WebSocket socket, StateSync sync, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open || await sync.ChangedAsync(cancellationToken) is not { } state) return;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new McpRunnerStateUpdate("state", state),
            McpRunnerJsonContext.Default.McpRunnerStateUpdate);
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static async Task SendDiagnosticsAsync(WebSocket socket, McpRunnerDiagnostics diagnostics)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var payload = JsonSerializer.SerializeToUtf8Bytes(diagnostics,
                McpRunnerJsonContext.Default.McpRunnerDiagnostics);
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException) { }
    }

    internal static ProcessStartInfo StartInfo(McpRunnerLaunch launch, RunnerSettings settings, string workDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = launch.Command,
            WorkingDirectory = workDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in launch.Arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var (name, value) in launch.Environment) start.Environment[name] = value;
        // Operator-chosen variables from the runner's own environment, such as an egress proxy or CA bundle.
        foreach (var (name, value) in settings.PassEnvironment) start.Environment[name] = value;
        // Set after the connector's keys so none of them can be overridden.
        start.Environment["PATH"] = settings.Path;
        start.Environment["HOME"] = workDirectory;
        start.Environment["TMPDIR"] = workDirectory;
        start.Environment["LANG"] = "C.UTF-8";
        if (launch.Script is not null)
            start.Environment[McpRunnerState.FileEnvironmentVariable] = Path.Combine(workDirectory, StateFileName);
        start.Environment["npm_config_cache"] = Path.Combine(settings.CacheRoot, "npm");
        start.Environment["npm_config_update_notifier"] = "false";
        start.Environment["npm_config_fund"] = "false";
        start.Environment["UV_CACHE_DIR"] = Path.Combine(settings.CacheRoot, "uv");
        start.Environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(settings.CacheRoot, "uv-python");
        start.Environment["UV_TOOL_DIR"] = Path.Combine(workDirectory, "uv-tools");
        return start;
    }

    /// <param name="beforeForward">Runs before each chunk is passed on, so a self-built server's state update
    /// reaches Jarvis ahead of the tool reply that follows it.</param>
    private static async Task PumpAsync(Stream source, Stream destination, CancellationToken cancellationToken,
        Func<Task>? beforeForward = null)
    {
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (beforeForward is not null) await beforeForward();
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
    }

    private static string PackageOf(McpRunnerLaunch launch) =>
        launch.Script is { } script ? "script:" + script.Sha256[..12]
        : launch.Arguments.LastOrDefault(argument => !argument.StartsWith('-')) ?? "";

    private static async Task Observe(Task task)
    {
        try { await task; }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or
                                              WebSocketException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string description)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.CloseOutputAsync(status, description.Length > 120 ? description[..120] : description,
                timeout.Token);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException) { }
    }

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started {Command} connector {Package}.")]
    private static partial void LogStarted(ILogger logger, string command, string package);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped {Command} connector {Package}.")]
    private static partial void LogStopped(ILogger logger, string command, string package);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a connector launch: {Reason}")]
    private static partial void LogRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start {Command} connector: {ErrorType}.")]
    private static partial void LogStartFailed(ILogger logger, string command, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Command} connector {Package} exited with code {ExitCode}.")]
    private static partial void LogExitFailure(ILogger logger, string command, string package, int exitCode);
}

/// <summary>
/// A self-built server's state file. It starts with what Jarvis saved after the last run; whenever the server
/// writes something different, <see cref="ChangedAsync"/> returns the new text (empty once the file is gone or
/// emptied). A file over the size limit is ignored, so the previous state stays saved.
/// </summary>
internal sealed class StateSync(string path, string? initial)
{
    private string _last = initial ?? "";

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _last.Length == 0 ? Task.CompletedTask : File.WriteAllTextAsync(path, _last, cancellationToken);

    public async Task<string?> ChangedAsync(CancellationToken cancellationToken)
    {
        string current;
        try
        {
            var file = new FileInfo(path);
            if (file.Exists && file.Length > McpRunnerState.MaxLength * 3L) return null;
            current = file.Exists ? (await File.ReadAllTextAsync(path, cancellationToken)).Trim() : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (current.Length > McpRunnerState.MaxLength || current == _last) return null;
        _last = current;
        return current;
    }
}

/// <summary>Keeps the last bytes a process wrote to stderr.</summary>
internal sealed class StderrTail(int capacity)
{
    private readonly Lock _gate = new();
    private readonly Queue<byte> _bytes = new();

    public async Task ReadAllAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            Append(buffer.AsSpan(0, read));
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            foreach (var value in data.Length > capacity ? data[^capacity..] : data)
            {
                _bytes.Enqueue(value);
                if (_bytes.Count > capacity) _bytes.Dequeue();
            }
        }
    }

    public string Text()
    {
        lock (_gate) return System.Text.Encoding.UTF8.GetString(_bytes.ToArray()).Trim();
    }
}

internal sealed record RunnerSettings(int MaxProcesses, TimeSpan MaxSession, string WorkRoot, string CacheRoot,
    string Path, IReadOnlyDictionary<string, string>? PassThrough = null)
{
    public IReadOnlyDictionary<string, string> PassEnvironment => PassThrough ?? new Dictionary<string, string>();

    public static RunnerSettings From(IConfiguration configuration) => new(
        Math.Clamp(configuration.GetValue("McpRunner:MaxProcesses", 16), 1, 128),
        TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("McpRunner:MaxSessionMinutes", 120), 1, 24 * 60)),
        configuration["McpRunner:WorkRoot"] ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcp-runner"),
        configuration["McpRunner:CacheRoot"] ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcp-runner-cache"),
        Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin",
        (configuration["McpRunner:PassEnvironment"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name is not ("PATH" or "HOME" or "TMPDIR"))
            .Select(name => (name, value: Environment.GetEnvironmentVariable(name)))
            .Where(pair => pair.value is not null)
            .ToDictionary(pair => pair.name, pair => pair.value!, StringComparer.Ordinal));
}
