using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents;

/// <summary>Runs a coding task in an isolated snapshot using the Codex CLI harness.</summary>
public sealed class CodexCodingTools(IConfiguration configuration, ILogger<CodexCodingTools> logger,
    IAuditEventStore audit, ICurrentUser currentUser, CodexProcessLimiter processLimiter)
{
    private const int MaxTaskLength = 16_000;
    private const int MaxResultLength = 24_000;

    [Description("Implement or investigate a coding task in an allowlisted repository. Jarvis copies the current tree into an isolated Git snapshot without history, so deleted credentials in the object store are unreachable. Changes remain there for review and are never merged into the main checkout automatically. This action always requires user approval.")]
    public async Task<string> RunCodingTaskAsync(
        [Description("Repository name from the configured Jarvis coding repository allowlist.")] string repositoryName,
        [Description("The coding task to perform. Include the intended outcome and relevant constraints.")] string task,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task) || task.Length > MaxTaskLength)
            throw new ArgumentException($"Coding task must contain 1 to {MaxTaskLength} characters.", nameof(task));

        var repository = GetRepositories().SingleOrDefault(item =>
            string.Equals(item.Name, repositoryName, StringComparison.Ordinal));
        if (repository is null)
            throw new ArgumentException("That repository is not in Jarvis's coding allowlist.", nameof(repositoryName));
        if (string.IsNullOrWhiteSpace(repository.Name) || repository.Name is "." or ".." ||
            repository.Name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new InvalidOperationException("The configured coding repository name is invalid.");

        var timeoutSeconds = configuration.GetValue("Coding:TimeoutSeconds", 900);
        if (timeoutSeconds is < 60 or > 3_600)
            throw new InvalidOperationException("Coding:TimeoutSeconds must be between 60 and 3600.");

        var repositoryPath = Path.GetFullPath(repository.Path);
        if (!Directory.Exists(repositoryPath) ||
            (!Directory.Exists(Path.Combine(repositoryPath, ".git")) && !File.Exists(Path.Combine(repositoryPath, ".git"))))
            throw new InvalidOperationException($"The configured coding repository '{repository.Name}' is unavailable or is not a Git checkout.");

        var configuredRoot = configuration["Coding:WorktreeRoot"];
        var repositoryParent = Directory.GetParent(repositoryPath)!.FullName;
        var worktreeRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(repositoryParent, ".jarvis-worktrees")
            : configuredRoot);
        if (!IsWithin(repositoryParent, worktreeRoot) || IsWithin(repositoryPath, worktreeRoot) || IsWithin(worktreeRoot, repositoryPath))
            throw new InvalidOperationException("Coding workspaces must be separate from the repository and kept beside it.");

        var taskId = Guid.CreateVersion7().ToString("N");
        var repositoryWorktreeRoot = Path.Combine(worktreeRoot, repository.Name);
        var worktreePath = Path.Combine(repositoryWorktreeRoot, taskId);
        Directory.CreateDirectory(repositoryWorktreeRoot);

        var files = await RunProcessAsync("git", repositoryPath,
            ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], cancellationToken);
        if (files.ExitCode != 0)
            throw new InvalidOperationException($"Could not list source files for the coding task: {Limit(files.StandardError, 1_500)}");
        // Never share the source repo object database: detached worktrees expose deleted blobs via git log/show.
        const string sourceMode = "filtered-file-snapshot";
        await CreateSnapshotRepositoryAsync(repositoryPath, worktreePath, files.StandardOutput, cancellationToken);

        var resultPath = Path.Combine(repositoryWorktreeRoot, $"{taskId}.result.txt");
        var executable = configuration["Codex:ExecutablePath"] ?? "codex";
        var codingModel = configuration["Codex:ModelClasses:Coding"];
        if (string.IsNullOrWhiteSpace(codingModel)) codingModel = configuration["Codex:Model"];

        var start = CreateMinimalProcessStart(executable, worktreePath);
        AddCodexCodingArguments(start, worktreePath, resultPath, codingModel);
        var metadata = JsonSerializer.Serialize(new { repository = repository.Name, sourceMode });

        await processLimiter.WaitAsync(cancellationToken);
        var exitCode = -1;
        var standardError = string.Empty;
        try
        {
            await audit.AppendAsync(currentUser.OwnerId, "codex", "coding_task.started", "high",
                true, null, metadata, cancellationToken);
            using var process = new Process { StartInfo = start };
            if (!process.Start())
            {
                await audit.AppendAsync(currentUser.OwnerId, "codex", "coding_task.failed", "high",
                    false, null, metadata, CancellationToken.None);
                throw new InvalidOperationException("Could not start the Codex CLI coding process.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var killOnCancellation = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });

            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            await process.StandardInput.WriteAsync(BuildPrompt(repository.Name, task).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Codex coding task timed out for repository {RepositoryName}.", repository.Name);
                await audit.AppendAsync(currentUser.OwnerId, "codex", "coding_task.failed", "high",
                    false, null, metadata, CancellationToken.None);
                try { await Task.WhenAll(stderrTask, stdoutTask).WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { }
                return JsonSerializer.Serialize(new
                {
                    completed = false,
                    reason = "Codex reached the coding-task time limit.",
                    worktreePath
                });
            }

            try { standardError = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { standardError = string.Empty; }
            try { _ = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { }
            exitCode = process.ExitCode;
        }
        finally
        {
            processLimiter.Release();
        }

        var summary = File.Exists(resultPath)
            ? await File.ReadAllTextAsync(resultPath, cancellationToken)
            : string.Empty;
        var status = await RunProcessAsync("git", worktreePath, ["status", "--short"], cancellationToken);
        var diff = await RunProcessAsync("git", worktreePath, ["diff", "--stat", "HEAD"], cancellationToken);
        await audit.AppendAsync(currentUser.OwnerId, "codex",
            exitCode == 0 ? "coding_task.completed" : "coding_task.failed",
            "high", exitCode == 0, null, metadata, cancellationToken);
        logger.LogInformation("Codex coding task finished for repository {RepositoryName} with exit code {ExitCode}.",
            repository.Name, exitCode);

        return JsonSerializer.Serialize(new
        {
            completed = exitCode == 0,
            exitCode,
            repository = repository.Name,
            sourceMode,
            worktreePath,
            changedFiles = Limit(status.StandardOutput.Trim(), 4_000),
            diffSummary = Limit(diff.StandardOutput.Trim(), 4_000),
            summary = Limit(summary.Trim(), MaxResultLength),
            error = exitCode == 0 ? null : Limit(standardError.Trim(), 2_000)
        });
    }

    private IReadOnlyList<CodingRepository> GetRepositories() =>
        configuration.GetSection("Coding:Repositories").Get<CodingRepository[]>() ?? [];

    private static ProcessStartInfo CreateMinimalProcessStart(string executable, string workingDirectory)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var inheritedPath = Environment.GetEnvironmentVariable("PATH");
        var home = Environment.GetEnvironmentVariable("HOME");
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(inheritedPath)) start.Environment["PATH"] = inheritedPath;
        if (!string.IsNullOrWhiteSpace(home)) start.Environment["HOME"] = home;
        if (!string.IsNullOrWhiteSpace(codexHome)) start.Environment["CODEX_HOME"] = codexHome;
        foreach (var key in new[] { "TMPDIR", "SSL_CERT_FILE", "SSL_CERT_DIR", "LANG", "LC_ALL" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[key] = value;
        }
        return start;
    }

    private static void AddCodexCodingArguments(ProcessStartInfo start, string worktreePath,
        string outputPath, string? model)
    {
        start.ArgumentList.Add("--ask-for-approval");
        start.ArgumentList.Add("never");
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--ephemeral");
        start.ArgumentList.Add("--ignore-user-config");
        start.ArgumentList.Add("--skip-git-repo-check");
        start.ArgumentList.Add("--sandbox");
        start.ArgumentList.Add("workspace-write");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("sandbox_workspace_write.network_access=false");
        start.ArgumentList.Add("--disable");
        start.ArgumentList.Add("computer_use");
        start.ArgumentList.Add("--disable");
        start.ArgumentList.Add("browser_use");
        start.ArgumentList.Add("--disable");
        start.ArgumentList.Add("browser_use_external");
        start.ArgumentList.Add("--disable");
        start.ArgumentList.Add("apps");
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(worktreePath);
        start.ArgumentList.Add("--output-last-message");
        start.ArgumentList.Add(outputPath);
        if (!string.IsNullOrWhiteSpace(model))
        {
            start.ArgumentList.Add("--model");
            start.ArgumentList.Add(model);
        }
        start.ArgumentList.Add("-");
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, string workingDirectory,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = CreateMinimalProcessStart(executable, workingDirectory);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {executable}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var killOnCancellation = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        });
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
            throw;
        }
        string stdout;
        string stderr;
        try { stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { stdout = string.Empty; }
        try { stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { stderr = string.Empty; }
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static async Task CreateSnapshotRepositoryAsync(string repositoryPath, string worktreePath,
        string fileList, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(worktreePath);
        var sourceRoot = Path.GetFullPath(repositoryPath) + Path.DirectorySeparatorChar;
        foreach (var listedPath in fileList.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
        {
            if (IsSensitivePath(listedPath)) continue;
            var relativePath = listedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var sourcePath = Path.GetFullPath(Path.Combine(repositoryPath, relativePath));
            if (!sourcePath.StartsWith(sourceRoot, PathComparison))
                throw new InvalidOperationException("Git returned a source file outside the configured repository.");
            if (!File.Exists(sourcePath)) continue;
            try
            {
                if ((File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            var targetPath = Path.Combine(worktreePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath);
        }

        var initialized = await RunProcessAsync("git", worktreePath, ["init", "--quiet"], cancellationToken);
        if (initialized.ExitCode != 0)
            throw new InvalidOperationException($"Could not initialize the isolated coding snapshot: {Limit(initialized.StandardError, 1_500)}");

        foreach (var (name, value) in new[]
        {
            ("user.name", "Jarvis Coding Workspace"),
            ("user.email", "jarvis-coding@localhost"),
            ("commit.gpgsign", "false")
        })
        {
            var configured = await RunProcessAsync("git", worktreePath, ["config", name, value], cancellationToken);
            if (configured.ExitCode != 0)
                throw new InvalidOperationException($"Could not configure the isolated coding snapshot: {Limit(configured.StandardError, 1_500)}");
        }

        var staged = await RunProcessAsync("git", worktreePath, ["add", "--all"], cancellationToken);
        if (staged.ExitCode != 0)
            throw new InvalidOperationException($"Could not prepare the isolated coding snapshot: {Limit(staged.StandardError, 1_500)}");
        var committed = await RunProcessAsync("git", worktreePath,
            ["-c", "core.hooksPath=/dev/null", "commit", "--quiet", "--allow-empty", "-m", "Jarvis coding snapshot"], cancellationToken);
        if (committed.ExitCode != 0)
            throw new InvalidOperationException($"Could not establish the isolated coding baseline: {Limit(committed.StandardError, 1_500)}");
    }

    internal static bool IsSensitivePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment.Equals(".ssh", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".aws", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".azure", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".kube", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".docker", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".codex", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals("secrets", StringComparison.OrdinalIgnoreCase)))
            return true;

        var name = segments.LastOrDefault() ?? string.Empty;
        if (name.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) &&
             !name.EndsWith(".example", StringComparison.OrdinalIgnoreCase) &&
             !name.EndsWith(".sample", StringComparison.OrdinalIgnoreCase) &&
             !name.EndsWith(".template", StringComparison.OrdinalIgnoreCase)))
            return true;

        var extension = Path.GetExtension(name);
        if (extension.Equals(".pem", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".key", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".p12", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jks", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".keystore", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Equals("id_rsa", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_dsa", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_ecdsa", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_ed25519", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_rsa_sk", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_dsa_sk", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_ecdsa_sk", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("id_ed25519_sk", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("auth.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("credentials.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("credentials", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("application_default_credentials.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".envrc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".npmrc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".yarnrc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".yarnrc.yml", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".pypirc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".netrc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("_netrc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".git-credentials", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".pgpass", StringComparison.OrdinalIgnoreCase);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string BuildPrompt(string repositoryName, string task) => $"""
        You are performing an approved coding task for Jarvis in the repository '{repositoryName}'.
        Work only inside the current Git worktree. Do not access credentials, home-directory files,
        production systems, or unrelated repositories. Do not use network services. Inspect the project,
        make the requested code changes, and leave all changes uncommitted for human review.
        Do not change files outside the user's request. When complete, summarize the implementation,
        files changed, and any build or static checks you actually ran. Never claim checks that were not run.

        User task:
        {task}
        """;

    private static bool IsWithin(string parentPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(parentPath, candidatePath);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record CodingRepository(string Name, string Path);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
