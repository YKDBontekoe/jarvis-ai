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
    IReadOnlyList<string> InputModalities);

public sealed record CodexInstallationStatus(
    string? InstalledVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    bool CanUpdate,
    string? UpdateBlockedReason,
    bool UsingManagedInstall,
    string? Error,
    IReadOnlyList<CodexSupportedModel> Models)
{
    public bool SupportsModel(string modelId) =>
        Models.Any(model => model.Model == modelId || model.Id == modelId);
}

/// <summary>
/// Reads the model catalog from the installed Codex CLI and installs a newer official release into the managed directory.
/// </summary>
public sealed partial class CodexInstallation(
    IHttpClientFactory httpClientFactory,
    CodexExecutable executable,
    CodexProcessLimiter processLimiter,
    ILogger<CodexInstallation> logger)
{
    private static readonly Uri LatestPackageUri = new("https://registry.npmjs.org/@openai/codex/latest");
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

    public async Task<CodexInstallationStatus> UpdateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var blocked = UpdateBlockedReason();
            if (blocked is not null) throw new InvalidOperationException(blocked);
            var latest = await GetLatestVersionAsync(cancellationToken);
            if (latest is null)
                throw new InvalidOperationException("The npm registry did not report a stable Codex CLI release.");

            var installed = await WithProcessSlot(() => ReadInstalledVersionAsync(executable.Resolve(), cancellationToken), cancellationToken);
            if (!IsNewerRelease(latest, installed))
            {
                _cached = null;
                return Cache(await BuildStatusAsync(cancellationToken));
            }

            await InstallAsync(latest, cancellationToken);
            _cached = null;
            var updated = Cache(await BuildStatusAsync(cancellationToken));
            logger.LogInformation("Updated the Codex CLI from {InstalledVersion} to {LatestVersion}.",
                installed, updated.InstalledVersion);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool IsStableRelease(string? version) =>
        version is not null && StableVersionPattern().IsMatch(version);

    internal static bool IsNewerRelease(string? latest, string? installed)
    {
        if (!TryParseRelease(latest, out var latestVersion)) return false;
        if (!TryParseRelease(installed, out var installedVersion)) return true;
        return latestVersion > installedVersion;
    }

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
            models.Add(new CodexSupportedModel(id, model, displayName,
                string.IsNullOrWhiteSpace(description) ? null : description,
                isDefault, hidden, modalities.Contains("image", StringComparer.OrdinalIgnoreCase), modalities));
        }
        return models
            .OrderByDescending(model => model.IsDefault)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
                models = await ListModelsAsync(resolved, cancellationToken);
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

        var latest = await GetLatestVersionAsync(cancellationToken);
        var blocked = UpdateBlockedReason();
        return new CodexInstallationStatus(installed, latest,
            blocked is null && IsNewerRelease(latest, installed),
            blocked is null, blocked, executable.IsManagedInstallActive, error, models);
    }

    private string? UpdateBlockedReason()
    {
        if (!executable.UsesStockCli)
            return "This server uses a custom Codex executable, so Settings cannot replace it.";
        if (executable.ResolveNpm() is null)
            return "npm is not available on this server, so Codex cannot be updated from Settings.";
        if (!executable.CanWriteManagedDirectory())
            return "Codex updates need a writable directory under CODEX_HOME.";
        return null;
    }

    private async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("npm-registry");
            using var response = await client.GetAsync(LatestPackageUri, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var version = document.RootElement.TryGetProperty("version", out var value) ? value.GetString() : null;
            return IsStableRelease(version) ? version : null;
        }
        catch (Exception exception) when (IsProbeFailure(exception, cancellationToken) || exception is JsonException)
        {
            logger.LogWarning(exception, "Could not read the latest Codex CLI version from the npm registry.");
            return null;
        }
    }

    private async Task InstallAsync(string version, CancellationToken cancellationToken)
    {
        if (!IsStableRelease(version))
            throw new InvalidOperationException("Refusing to install an unexpected Codex version.");
        var npm = executable.ResolveNpm()
            ?? throw new InvalidOperationException("npm is not available on this server, so Codex cannot be updated from Settings.");
        Directory.CreateDirectory(executable.ManagedDirectory);
        var start = CreateProcess(npm, executable.ManagedDirectory);
        start.Environment["npm_config_prefix"] = executable.ManagedDirectory;
        start.Environment["npm_config_cache"] = Path.Combine(Path.GetTempPath(), "jarvis-npm-cache");
        start.Environment["npm_config_update_notifier"] = "false";
        start.Environment["npm_config_fund"] = "false";
        start.Environment["npm_config_audit"] = "false";
        foreach (var argument in new[]
        {
            "install", "--global", "--prefix", executable.ManagedDirectory,
            "--no-fund", "--no-audit", "--no-update-notifier", "@openai/codex@" + version
        })
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start npm.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var kill = timeout.Token.Register(() => Kill(process));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The Codex update timed out.");
        }
        if (process.ExitCode != 0)
        {
            var stderr = await SafeRead(stderrTask);
            var stdout = await SafeRead(stdoutTask);
            throw new InvalidOperationException("Codex update failed: " + Limit(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
        }
        if (executable.FindManagedExecutable() is null)
            throw new InvalidOperationException("Codex update finished without installing a CLI binary.");
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

    private static async Task<IReadOnlyList<CodexSupportedModel>> ListModelsAsync(string executablePath,
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
            return await connection.ListModelsAsync(timeout.Token);
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

    private static bool TryParseRelease(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (!IsStableRelease(text)) return false;
        var parts = text!.Split('.');
        version = new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        return true;
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
            var requestId = await WriteAsync("initialize", new { clientInfo = new { name = "jarvis", version = "1.0.0" } }, cancellationToken);
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
                if (line is null) throw new InvalidOperationException("Codex CLI app-server exited before listing models.");
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
