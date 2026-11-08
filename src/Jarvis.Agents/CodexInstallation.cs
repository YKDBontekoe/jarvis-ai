using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents;

public sealed record CodexSupportedModel(
    string Id,
    string Model,
    string DisplayName,
    string? Description,
    bool IsDefault,
    bool Hidden,
    bool SupportsImages,
    IReadOnlyList<string> InputModalities,
    IReadOnlyList<ReasoningEffortOption> SupportedReasoningEfforts,
    string? DefaultReasoningEffort);

public sealed record ReasoningEffortOption(string ReasoningEffort, string Description);

public sealed record CodexRealtimeVoice(string Id, string Name, bool IsDefault);

public sealed record CodexRealtimeVoices(IReadOnlyList<CodexRealtimeVoice> Voices, string? DefaultVoice)
{
    public static CodexRealtimeVoices Empty { get; } = new([], null);

    public bool Supports(string voice) =>
        Voices.Any(item => item.Id.Equals(voice.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record CodexInstallationStatus(
    string? InstalledVersion,
    string PinnedVersion,
    bool UsingManagedInstall,
    string? Error,
    IReadOnlyList<CodexSupportedModel> Models,
    CodexRealtimeVoices Voices,
    string? VoiceError)
{
    public bool SupportsModel(string modelId) =>
        Models.Any(model => model.Model == modelId || model.Id == modelId);

    /// <summary>True when the running CLI is the version Jarvis is built and tested against.</summary>
    public bool MatchesPinnedVersion => InstalledVersion == PinnedVersion;
}

/// <summary>
/// The Codex CLI release Jarvis is built and tested against. Jarvis relies on its app-server protocol (service tiers,
/// thread continuation), so the version is managed here, not chosen by the owner. infra/compose/Dockerfile installs
/// the same version, and a unit test keeps the two in step. Change both together, after checking the protocol.
/// </summary>
public static class CodexVersion
{
    public const string Pinned = "0.167.0";
}

/// <summary>
/// Reads the model catalog from the installed Codex CLI and keeps the CLI at <see cref="CodexVersion.Pinned"/>.
/// </summary>
public sealed partial class CodexInstallation(
    CodexExecutable executable,
    CodexProcessLimiter processLimiter,
    ILogger<CodexInstallation> logger)
{
    private static readonly TimeSpan CatalogCacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ErrorCacheLifetime = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CodexInstallationStatus? _cached;
    private DateTimeOffset _cachedUntil;

    public async Task<CodexInstallationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && _cachedUntil > DateTimeOffset.UtcNow) return _cached;
            return Cache(await BuildStatusAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Makes the CLI Jarvis runs the pinned release. An install under the managed directory with another version (one
    /// an earlier Jarvis let the owner choose) is removed; when the stock executable is another version too, the pinned
    /// release is installed there. A custom executable (a wrapper or test fixture) is left alone. The API and the
    /// worker share the managed directory, so a lock file keeps them from installing at the same time.
    /// </summary>
    public async Task<string> EnsurePinnedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!executable.UsesStockCli)
                return "custom Codex executable; version not managed";
            Directory.CreateDirectory(Path.GetDirectoryName(executable.ManagedDirectory)!);
            await using var installLock = await AcquireInstallLockAsync(cancellationToken);
            if (executable.FindManagedExecutable() is { } managed)
            {
                var managedVersion = await TryReadVersionAsync(managed, cancellationToken);
                if (managedVersion == CodexVersion.Pinned) return "managed install is " + CodexVersion.Pinned;
                logger.LogInformation("Removing the managed Codex CLI {Version}; Jarvis runs {Pinned}.",
                    managedVersion ?? "of unknown version", CodexVersion.Pinned);
                DeleteDirectory(executable.ManagedDirectory);
                CodexCliChatClient.RetireProcesses();
            }

            var stockVersion = await TryReadVersionAsync(executable.ConfiguredPath, cancellationToken);
            if (stockVersion == CodexVersion.Pinned) return "installed CLI is " + CodexVersion.Pinned;
            if (executable.ResolveNpm() is null || !executable.CanWriteManagedDirectory())
            {
                logger.LogWarning("The Codex CLI is {Version}, not the {Pinned} Jarvis runs, and Jarvis cannot install it here (needs npm and a writable CODEX_HOME).",
                    stockVersion ?? "missing", CodexVersion.Pinned);
                return "cannot install " + CodexVersion.Pinned;
            }
            await InstallAsync(CodexVersion.Pinned, cancellationToken);
            CodexCliChatClient.RetireProcesses();
            _cached = null;
            logger.LogInformation("Installed the Codex CLI {Pinned} (was {Version}).", CodexVersion.Pinned,
                stockVersion ?? "missing");
            return "installed " + CodexVersion.Pinned;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> TryReadVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await WithProcessSlot(() => ReadInstalledVersionAsync(path, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (IsProbeFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private async Task<FileStream> AcquireInstallLockAsync(CancellationToken cancellationToken)
    {
        var path = executable.ManagedDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".lock";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < 360)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    internal static bool IsStableRelease(string? version) =>
        version is not null && StableVersionPattern().IsMatch(version);

    internal static string? ReadVersion(string output)
    {
        var match = VersionSearchPattern().Match(output);
        return match.Success ? match.Value : null;
    }

    internal static IReadOnlyList<CodexSupportedModel> ParseModels(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) return [];
        var models = new List<CodexSupportedModel>();
        foreach (var item in data.EnumerateArray())
        {
            var model = ReadString(item, "model");
            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(model)) model = id;
            if (string.IsNullOrWhiteSpace(id)) id = model;
            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(id)) continue;
            var displayName = ReadString(item, "displayName");
            if (string.IsNullOrWhiteSpace(displayName)) displayName = model;
            var description = ReadString(item, "description");
            var hidden = item.TryGetProperty("hidden", out var hiddenElement) && hiddenElement.ValueKind == JsonValueKind.True;
            var isDefault = item.TryGetProperty("isDefault", out var defaultElement) && defaultElement.ValueKind == JsonValueKind.True;
            IReadOnlyList<string> modalities;
            if (item.TryGetProperty("inputModalities", out var modalitiesElement) &&
                modalitiesElement.ValueKind == JsonValueKind.Array)
            {
                modalities = modalitiesElement.EnumerateArray()
                    .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            else
            {
                modalities = ["text", "image"];
            }
            var efforts = item.TryGetProperty("supportedReasoningEfforts", out var effortsElement) &&
                          effortsElement.ValueKind == JsonValueKind.Array
                ? effortsElement.EnumerateArray()
                    .Select(value => new ReasoningEffortOption(
                        ReadString(value, "reasoningEffort") ?? string.Empty,
                        ReadString(value, "description") ?? string.Empty))
                    .Where(value => !string.IsNullOrWhiteSpace(value.ReasoningEffort))
                    .ToArray()
                : [];
            var defaultEffort = ReadString(item, "defaultReasoningEffort");
            models.Add(new CodexSupportedModel(id, model, displayName,
                string.IsNullOrWhiteSpace(description) ? null : description,
                isDefault, hidden, modalities.Contains("image", StringComparer.OrdinalIgnoreCase), modalities,
                efforts, defaultEffort));
        }
        return models
            .OrderByDescending(model => model.IsDefault)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static CodexRealtimeVoices ParseVoices(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("voices", out var voices) || voices.ValueKind != JsonValueKind.Object)
            return CodexRealtimeVoices.Empty;
        var defaultVoice = NormalizeVoiceId(ReadString(voices, "defaultV1"));
        var parsed = new List<CodexRealtimeVoice>();
        if (voices.TryGetProperty("v1", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = NormalizeVoiceId(item.ValueKind == JsonValueKind.String ? item.GetString() : null);
                if (id is null || parsed.Any(voice => voice.Id == id)) continue;
                parsed.Add(new CodexRealtimeVoice(id, DisplayVoice(id), id == defaultVoice));
            }
        }
        if (defaultVoice is not null && parsed.All(voice => voice.Id != defaultVoice))
            parsed.Insert(0, new CodexRealtimeVoice(defaultVoice, DisplayVoice(defaultVoice), true));
        return new CodexRealtimeVoices(parsed, defaultVoice);
    }

    private static string? NormalizeVoiceId(string? voice)
    {
        var id = voice?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static string DisplayVoice(string id) =>
        id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..];

    private CodexInstallationStatus Cache(CodexInstallationStatus status)
    {
        _cached = status;
        _cachedUntil = DateTimeOffset.UtcNow + (status.Error is null ? CatalogCacheLifetime : ErrorCacheLifetime);
        return status;
    }

    private async Task<CodexInstallationStatus> BuildStatusAsync(CancellationToken cancellationToken)
    {
        var resolved = executable.Resolve();
        string? installed = null;
        string? error = null;
        IReadOnlyList<CodexSupportedModel> models = [];
        var voices = CodexRealtimeVoices.Empty;
        string? voiceError = null;
        await processLimiter.WaitAsync(cancellationToken);
        try
        {
            try
            {
                installed = await ReadInstalledVersionAsync(resolved, cancellationToken);
            }
            catch (Exception exception) when (IsProbeFailure(exception, cancellationToken))
            {
                logger.LogWarning(exception, "Could not read the installed Codex CLI version.");
            }

            try
            {
                var catalog = await ListCatalogAsync(resolved, cancellationToken);
                models = catalog.Models;
                voices = catalog.Voices;
                voiceError = catalog.VoiceError;
            }
            catch (Exception exception) when (IsProbeFailure(exception, cancellationToken))
            {
                logger.LogWarning(exception, "Could not load the Codex CLI model catalog.");
                error = "Could not load the models supported by the installed Codex CLI.";
            }
        }
        finally
        {
            processLimiter.Release();
        }

        return new CodexInstallationStatus(installed, CodexVersion.Pinned, executable.IsManagedInstallActive, error,
            models, voices, voiceError);
    }

    /// <summary>
    /// Installs <paramref name="version"/> beside the managed directory and moves it into place only once complete,
    /// so a model call never resolves a half-installed CLI.
    /// </summary>
    private async Task InstallAsync(string version, CancellationToken cancellationToken)
    {
        if (!IsStableRelease(version))
            throw new InvalidOperationException("Refusing to install an unexpected Codex version.");
        var npm = executable.ResolveNpm()
            ?? throw new InvalidOperationException("npm is not available on this server, so Codex cannot be installed.");
        var staging = executable.ManagedDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".installing";
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);
        try
        {
            var start = CreateProcess(npm, staging);
            start.Environment["npm_config_prefix"] = staging;
            start.Environment["npm_config_cache"] = Path.Combine(Path.GetTempPath(), "jarvis-npm-cache");
            start.Environment["npm_config_update_notifier"] = "false";
            start.Environment["npm_config_fund"] = "false";
            start.Environment["npm_config_audit"] = "false";
            foreach (var argument in new[]
            {
                "install", "--global", "--prefix", staging,
                "--no-fund", "--no-audit", "--no-update-notifier", "@openai/codex@" + version
            })
                start.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new InvalidOperationException("Could not start npm.");
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var kill = timeout.Token.Register(() => Kill(process));
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("The Codex install timed out.");
            }
            if (process.ExitCode != 0)
            {
                var stderr = await SafeRead(stderrTask);
                var stdout = await SafeRead(stdoutTask);
                throw new InvalidOperationException("Codex install failed: " + Limit(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
            }
            if (CodexExecutable.FindExecutableIn(staging) is not { } installed ||
                await TryReadVersionAsync(installed, cancellationToken) != version)
                throw new InvalidOperationException($"The Codex install did not produce a {version} CLI binary.");
            DeleteDirectory(executable.ManagedDirectory);
            Directory.Move(staging, executable.ManagedDirectory);
        }
        finally
        {
            try { DeleteDirectory(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<T> WithProcessSlot<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await processLimiter.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            processLimiter.Release();
        }
    }

    private static async Task<string?> ReadInstalledVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("jarvis-codex-version-").FullName;
        try
        {
            using var process = new Process { StartInfo = CreateProcess(executablePath, directory) };
            process.StartInfo.ArgumentList.Add("--version");
            if (!process.Start()) throw new InvalidOperationException("Could not start the Codex CLI.");
            // Nothing is sent: close stdin so a CLI or wrapper that reads it exits instead of waiting out the timeout.
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var kill = timeout.Token.Register(() => Kill(process));
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Codex CLI version check failed: " + Limit(stderr));
            return ReadVersion(stdout + "\n" + stderr);
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record CatalogProbe(IReadOnlyList<CodexSupportedModel> Models, CodexRealtimeVoices Voices, string? VoiceError);

    private static async Task<CatalogProbe> ListCatalogAsync(string executablePath,
        CancellationToken cancellationToken)
    {
        var scratch = Directory.CreateTempSubdirectory("jarvis-codex-models-").FullName;
        using var process = new Process { StartInfo = CodexCliChatClient.CreateAppServerStart(executablePath, scratch, false) };
        if (!process.Start()) throw new InvalidOperationException("Could not start the Codex CLI app-server.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var kill = timeout.Token.Register(() => Kill(process));
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            using var writer = process.StandardInput;
            using var reader = process.StandardOutput;
            var connection = new CatalogConnection(writer, reader);
            await connection.InitializeAsync(timeout.Token);
            var models = await connection.ListModelsAsync(timeout.Token);
            try
            {
                return new CatalogProbe(models, await connection.ListVoicesAsync(timeout.Token), null);
            }
            catch (Exception exception) when (IsProbeFailure(exception, cancellationToken) || exception is JsonException)
            {
                return new CatalogProbe(models, CodexRealtimeVoices.Empty,
                    "Could not load the voices supported by the installed Codex CLI.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Codex CLI model list timed out." + Suffix(await SafeRead(stderrTask)));
        }
        catch (InvalidOperationException exception)
        {
            var stderr = await SafeRead(stderrTask);
            throw new InvalidOperationException(exception.Message + Suffix(stderr), exception);
        }
        finally
        {
            Kill(process);
            try { Directory.Delete(scratch, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static ProcessStartInfo CreateProcess(string fileName, string workingDirectory)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment.Clear();
        foreach (var key in new[] { "PATH", "HOME", "CODEX_HOME", "TMPDIR", "SSL_CERT_FILE", "SSL_CERT_DIR", "LANG", "LC_ALL" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[key] = value;
        }
        if (!start.Environment.ContainsKey("HOME")) start.Environment["HOME"] = Path.GetTempPath();
        return start;
    }

    private static bool IsProbeFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is InvalidOperationException or IOException or Win32Exception or HttpRequestException ||
        (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static async Task<string> SafeRead(Task<string> output)
    {
        try { return await output; }
        catch (OperationCanceledException) { return ""; }
        catch (IOException) { return ""; }
        catch (ObjectDisposedException) { return ""; }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }


    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Limit(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }

    private static string Suffix(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : " " + Limit(value);

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex StableVersionPattern();

    [GeneratedRegex(@"(?<!\d)[0-9]+\.[0-9]+\.[0-9]+(?!\d)")]
    private static partial Regex VersionSearchPattern();

    private sealed class CatalogConnection(StreamWriter writer, StreamReader reader)
    {
        private int _requestId;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var requestId = await WriteAsync("initialize", new
            {
                clientInfo = new { name = "jarvis", version = "1.0.0" },
                capabilities = new { experimentalApi = true }
            }, cancellationToken);
            using var response = await ReadResponseAsync(requestId, cancellationToken);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { method = "initialized", @params = new { } }).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CodexSupportedModel>> ListModelsAsync(CancellationToken cancellationToken)
        {
            var models = new List<CodexSupportedModel>();
            string? cursor = null;
            do
            {
                var requestId = await WriteAsync("model/list", new { includeHidden = true, limit = 100, cursor }, cancellationToken);
                using var response = await ReadResponseAsync(requestId, cancellationToken);
                var result = response.RootElement.GetProperty("result");
                if (result.TryGetProperty("data", out var data))
                    models.AddRange(ParseModels(data));
                cursor = result.TryGetProperty("nextCursor", out var nextCursor) && nextCursor.ValueKind == JsonValueKind.String
                    ? nextCursor.GetString()
                    : null;
            }
            while (!string.IsNullOrWhiteSpace(cursor) && models.Count < 1_000);
            return models
                .OrderByDescending(model => model.IsDefault)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public async Task<CodexRealtimeVoices> ListVoicesAsync(CancellationToken cancellationToken)
        {
            var requestId = await WriteAsync("thread/realtime/listVoices", new { }, cancellationToken);
            using var response = await ReadResponseAsync(requestId, cancellationToken);
            return ParseVoices(response.RootElement.GetProperty("result"));
        }

        private async Task<int> WriteAsync(string method, object parameters, CancellationToken cancellationToken)
        {
            var id = ++_requestId;
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { method, id, @params = parameters }, JsonOptions).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return id;
        }

        private async Task<JsonDocument> ReadResponseAsync(int requestId, CancellationToken cancellationToken)
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) throw new InvalidOperationException("Codex CLI app-server exited before answering.");
                var message = JsonDocument.Parse(line);
                if (!message.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt32(out var responseId) || responseId != requestId)
                {
                    message.Dispose();
                    continue;
                }
                if (message.RootElement.TryGetProperty("error", out var error))
                {
                    var reason = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var messageText)
                        ? messageText.GetString()
                        : error.ToString();
                    message.Dispose();
                    throw new InvalidOperationException("Codex CLI app-server request failed: " + Limit(reason));
                }
                return message;
            }
        }
    }
}
