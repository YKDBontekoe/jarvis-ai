using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jarvis.Application.Audit;
using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Coding;

/// <summary>
/// Publishes a coding run's changes as a branch and GitHub pull request. The coding run itself is an isolated,
/// history-free snapshot with the network disabled; only this service talks to the code host, and only with the
/// owner's stored GitHub token. It never pushes to a base branch and never force-pushes.
/// </summary>
public sealed partial class GitHubCodingPullRequestService(
    ICodingRunStore runs,
    IIntegrationCredentialStore credentials,
    IAuditEventStore audit,
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    ILogger<GitHubCodingPullRequestService> logger) : ICodingPullRequestService
{
    private const int MaxDisplayedPatch = 400_000;
    private const int MaxPublishedPatch = 4_000_000;
    private const string CredentialProvider = "github";
    private const string CredentialSecret = "token";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public bool CanPublish(string repository) => FindRepository(repository) is not null;

    public async Task<CodingDiff> GetDiffAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(runId, ownerId, cancellationToken);
        var (patch, files) = await ReadWorktreeChangesAsync(run, cancellationToken);
        var truncated = patch.Length > MaxDisplayedPatch;
        var warnings = new List<string>();
        if (files.Any(file => CodingPathPolicy.IsBlocked(file.Path)))
            warnings.Add("This change touches files Jarvis will not publish (CI, secrets, or git internals).");
        if (files.Any(file => file.Protected))
            warnings.Add("This change touches security-sensitive code. Read those files closely before merging.");
        return new CodingDiff(truncated ? patch[..MaxDisplayedPatch] : patch, files, truncated, warnings);
    }

    public async Task<CodingPullRequestStatus> PublishAsync(Guid runId, Guid ownerId, string? title, string? body,
        CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(runId, ownerId, cancellationToken);
        var repository = FindRepository(run.Repository)
                         ?? throw new CodingPullRequestException("not_configured",
                             "This repository has no GitHub pull request target configured.");
        using var gate = await AcquireAsync(runId, cancellationToken);
        run = await RequireRunAsync(runId, ownerId, cancellationToken);
        if (run.PullRequestNumber is not null && run.PullRequestState == "open")
            return await RequireStatusAsync(run, repository, ownerId, cancellationToken);
        if (run.Status != "completed")
            throw new CodingPullRequestException("run_not_ready", "Only a finished coding run can open a pull request.");

        var token = await RequireTokenAsync(ownerId, cancellationToken);
        var (patch, files) = await ReadWorktreeChangesAsync(run, cancellationToken);
        if (files.Count == 0 || string.IsNullOrWhiteSpace(patch))
            throw new CodingPullRequestException("no_changes", "This run did not change any files.");
        if (patch.Length > MaxPublishedPatch)
            throw new CodingPullRequestException("too_large", "This change is too large to publish automatically.");
        var blocked = files.Where(file => CodingPathPolicy.IsBlocked(file.Path)).Select(file => file.Path).ToArray();
        if (blocked.Length != 0)
            throw new CodingPullRequestException("protected_paths",
                "Jarvis will not publish changes to: " + string.Join(", ", blocked.Take(5)));

        var prTitle = CleanTitle(title, run);
        var branch = BranchName(run.Id, prTitle, DateTimeOffset.UtcNow);
        var scratch = Directory.CreateTempSubdirectory("jarvis-pr-").FullName;
        var pushed = false;
        Dictionary<string, string>? pushEnvironment = null;
        try
        {
            var clone = Path.Combine(scratch, "repo");
            var env = GitEnvironment(repository, token, scratch);
            await GitAsync(scratch, env, ["clone", "--quiet", "--depth", "1", "--branch", repository.BaseBranch,
                repository.RemoteUrl, clone], cancellationToken, "clone the repository");
            await GitAsync(clone, env, ["checkout", "--quiet", "-b", branch], cancellationToken, "create the branch");
            var patchFile = Path.Combine(scratch, "change.patch");
            await File.WriteAllTextAsync(patchFile, patch, new UTF8Encoding(false), cancellationToken);
            await GitAsync(clone, env, ["apply", "--index", "--whitespace=nowarn", patchFile], cancellationToken,
                "apply the change to the latest " + repository.BaseBranch);
            await GitAsync(clone, env, ["-c", "user.name=Jarvis", "-c", "user.email=jarvis@users.noreply.github.com",
                "-c", "commit.gpgsign=false", "-c", "core.hooksPath=/dev/null", "commit", "--quiet", "-m",
                CommitMessage(prTitle, run)], cancellationToken, "commit the change");
            pushEnvironment = env;
            await GitAsync(clone, env, ["push", "--quiet", "origin", "HEAD:refs/heads/" + branch], cancellationToken,
                "push the branch");
            pushed = true;

            var created = await SendAsync<GitHubPullRequest>(repository, token, HttpMethod.Post,
                $"repos/{repository.GitHubRepository}/pulls",
                new
                {
                    title = prTitle,
                    head = branch,
                    @base = repository.BaseBranch,
                    body = string.IsNullOrWhiteSpace(body) ? BuildBody(run, files) : body.Trim(),
                    maintainer_can_modify = false
                }, cancellationToken);
            await runs.SetPullRequestAsync(run.Id, ownerId, branch, repository.GitHubRepository, created.Number,
                created.HtmlUrl, "open", cancellationToken);
            await audit.AppendAsync(ownerId, "coding", "coding_pr.opened", "high", true, null,
                JsonSerializer.Serialize(new { run = run.Id, repository = repository.GitHubRepository, pr = created.Number }),
                cancellationToken);
            return await ToStatusAsync(repository, token, created, cancellationToken);
        }
        catch (CodingPullRequestException)
        {
            await DeletePushedBranchAsync(scratch, pushEnvironment, branch, pushed);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not open a pull request for coding run {RunId}.", runId);
            await DeletePushedBranchAsync(scratch, pushEnvironment, branch, pushed);
            throw new CodingPullRequestException("publish_failed", "Jarvis could not open the pull request.");
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    public async Task<CodingPullRequestStatus?> GetStatusAsync(Guid runId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(runId, ownerId, cancellationToken);
        if (run.PullRequestNumber is null) return null;
        var repository = FindRepository(run.Repository)
                         ?? throw new CodingPullRequestException("not_configured",
                             "This repository has no GitHub pull request target configured.");
        return await RequireStatusAsync(run, repository, ownerId, cancellationToken);
    }

    public async Task<CodingPullRequestStatus> MergeAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(runId, ownerId, cancellationToken);
        var repository = FindRepository(run.Repository)
                         ?? throw new CodingPullRequestException("not_configured", "No pull request target is configured.");
        using var gate = await AcquireAsync(runId, cancellationToken);
        var status = await RequireStatusAsync(run, repository, ownerId, cancellationToken);
        if (status.Merged) return status;
        if (status.State != "open")
            throw new CodingPullRequestException("not_open", "This pull request is closed.");
        if (status.ChecksState is "failure" or "pending")
            throw new CodingPullRequestException("checks_not_passing",
                status.ChecksState == "pending" ? "Checks are still running." : "Checks are failing.");
        if (status.Mergeable == false)
            throw new CodingPullRequestException("not_mergeable", "GitHub reports merge conflicts.");

        var token = await RequireTokenAsync(ownerId, cancellationToken);
        // Pinning the head SHA makes GitHub reject the merge if the branch changed after the owner reviewed it.
        await SendAsync<JsonElement>(repository, token, HttpMethod.Put,
            $"repos/{repository.GitHubRepository}/pulls/{status.Number}/merge",
            new { merge_method = "squash", sha = status.HeadSha }, cancellationToken);
        await runs.SetPullRequestAsync(run.Id, ownerId, run.BranchName, repository.GitHubRepository, status.Number,
            status.Url, "merged", cancellationToken);
        await audit.AppendAsync(ownerId, "coding", "coding_pr.merged", "high", true, null,
            JsonSerializer.Serialize(new { run = run.Id, repository = repository.GitHubRepository, pr = status.Number }),
            cancellationToken);
        return status with { State = "merged", Merged = true };
    }

    public async Task<CodingPullRequestStatus> CloseAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(runId, ownerId, cancellationToken);
        var repository = FindRepository(run.Repository)
                         ?? throw new CodingPullRequestException("not_configured", "No pull request target is configured.");
        using var gate = await AcquireAsync(runId, cancellationToken);
        var status = await RequireStatusAsync(run, repository, ownerId, cancellationToken);
        if (status.State != "open") return status;
        var token = await RequireTokenAsync(ownerId, cancellationToken);
        await SendAsync<JsonElement>(repository, token, HttpMethod.Patch,
            $"repos/{repository.GitHubRepository}/pulls/{status.Number}", new { state = "closed" }, cancellationToken);
        await runs.SetPullRequestAsync(run.Id, ownerId, run.BranchName, repository.GitHubRepository, status.Number,
            status.Url, "closed", cancellationToken);
        await audit.AppendAsync(ownerId, "coding", "coding_pr.closed", "medium", true, null,
            JsonSerializer.Serialize(new { run = run.Id, repository = repository.GitHubRepository, pr = status.Number }),
            cancellationToken);
        return status with { State = "closed" };
    }

    private async Task<CodingPullRequestStatus> RequireStatusAsync(CodingRunRecord run, PullRequestTarget repository,
        Guid ownerId, CancellationToken cancellationToken)
    {
        var token = await RequireTokenAsync(ownerId, cancellationToken);
        var pr = await SendAsync<GitHubPullRequest>(repository, token, HttpMethod.Get,
            $"repos/{repository.GitHubRepository}/pulls/{run.PullRequestNumber}", null, cancellationToken);
        var state = pr.Merged ? "merged" : pr.State;
        if (state != run.PullRequestState)
            await runs.SetPullRequestAsync(run.Id, ownerId, run.BranchName, repository.GitHubRepository, pr.Number,
                pr.HtmlUrl, state, cancellationToken);
        return await ToStatusAsync(repository, token, pr, cancellationToken);
    }

    private async Task<CodingPullRequestStatus> ToStatusAsync(PullRequestTarget repository, string token,
        GitHubPullRequest pr, CancellationToken cancellationToken)
    {
        var checks = new List<CodingCheck>();
        var checksState = "none";
        if (!string.IsNullOrWhiteSpace(pr.Head?.Sha))
        {
            try
            {
                var response = await SendAsync<GitHubCheckRuns>(repository, token, HttpMethod.Get,
                    $"repos/{repository.GitHubRepository}/commits/{pr.Head.Sha}/check-runs?per_page=30", null,
                    cancellationToken);
                checks.AddRange(response.CheckRuns.Select(run => new CodingCheck(run.Name, run.Status, run.Conclusion)));
                checksState = SummarizeChecks(checks);
            }
            catch (CodingPullRequestException)
            {
                // Checks are informational; a failure to read them should not hide the pull request.
            }
        }

        return new CodingPullRequestStatus(pr.Number, pr.HtmlUrl, repository.GitHubRepository, pr.Title,
            pr.Merged ? "merged" : pr.State, pr.Merged, pr.Mergeable, pr.MergeableState, pr.Head?.Sha, checksState,
            checks);
    }

    public static string SummarizeChecks(IReadOnlyCollection<CodingCheck> checks)
    {
        if (checks.Count == 0) return "none";
        if (checks.Any(check => check.Conclusion is "failure" or "timed_out" or "cancelled" or "action_required"))
            return "failure";
        if (checks.Any(check => check.Status != "completed")) return "pending";
        return "success";
    }

    private async Task<T> SendAsync<T>(PullRequestTarget repository, string token, HttpMethod method, string path,
        object? body, CancellationToken cancellationToken)
    {
        using var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(repository.ApiBaseUrl.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Jarvis", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("GitHub {Method} {Path} returned {Status}: {Detail}", method, path,
                (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail);
            var (code, message) = (int)response.StatusCode switch
            {
                401 => ("credentials_invalid", "GitHub rejected the stored token. Save a new one and retry."),
                403 => ("credentials_forbidden", "The GitHub token does not allow this. It needs repository contents and pull request access."),
                404 => ("github_not_found", "GitHub could not find that repository or pull request."),
                405 or 409 or 422 => ("github_rejected", "GitHub would not accept that change (it may have moved on or need a rebase)."),
                _ => ("github_error", "GitHub is unavailable right now.")
            };
            throw new CodingPullRequestException(code, message);
        }

        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private async Task<(string Patch, IReadOnlyList<CodingDiffFile> Files)> ReadWorktreeChangesAsync(
        CodingRunRecord run, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(run.WorktreePath) || !Directory.Exists(Path.Combine(run.WorktreePath, ".git")))
            throw new CodingPullRequestException("worktree_missing", "The workspace for this run is no longer available.");
        var env = GitEnvironment(null, null, run.WorktreePath);
        await GitAsync(run.WorktreePath, env, ["add", "--all"], cancellationToken, "read the changes");
        var patch = await GitAsync(run.WorktreePath, env,
            ["diff", "--cached", "--binary", "--no-color", "--no-ext-diff", "HEAD"], cancellationToken, "read the diff");
        var names = await GitAsync(run.WorktreePath, env,
            ["diff", "--cached", "--name-status", "--no-renames", "-z", "HEAD"], cancellationToken, "list the changes");
        var parts = names.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<CodingDiffFile>();
        for (var index = 0; index + 1 < parts.Length; index += 2)
        {
            var status = parts[index] switch { "A" => "added", "D" => "deleted", _ => "modified" };
            files.Add(new CodingDiffFile(parts[index + 1], status, CodingPathPolicy.IsProtected(parts[index + 1])));
        }

        return (patch, files);
    }

    private async Task<CodingRunRecord> RequireRunAsync(Guid runId, Guid ownerId, CancellationToken cancellationToken) =>
        await runs.GetAsync(runId, ownerId, cancellationToken)
        ?? throw new CodingPullRequestException("not_found", "That coding run was not found.");

    private async Task<string> RequireTokenAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var secrets = await credentials.GetSecretsAsync(ownerId, CredentialProvider, cancellationToken);
        if (secrets is null || !secrets.TryGetValue(CredentialSecret, out var token) || string.IsNullOrWhiteSpace(token))
            throw new CodingPullRequestException("credentials_missing",
                "Save a GitHub token (Settings → Integrations → GitHub) so Jarvis can open pull requests.");
        return token;
    }

    /// <summary>A branch pushed for a pull request that then failed to open would block the retry, so remove it.</summary>
    private async Task DeletePushedBranchAsync(string scratch, Dictionary<string, string>? environment, string branch,
        bool pushed)
    {
        if (!pushed || environment is null) return;
        try
        {
            await GitAsync(Path.Combine(scratch, "repo"), environment,
                ["push", "--quiet", "origin", "--delete", branch], CancellationToken.None, "remove the unused branch");
        }
        catch (CodingPullRequestException)
        {
            logger.LogWarning("Could not remove the unused branch {Branch} after a failed pull request.", branch);
        }
    }

    private static async Task<IDisposable> AcquireAsync(Guid runId, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(runId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private PullRequestTarget? FindRepository(string name)
    {
        foreach (var section in configuration.GetSection("Coding:Repositories").GetChildren())
        {
            if (!string.Equals(section["Name"], name, StringComparison.Ordinal)) continue;
            var github = section["GitHubRepository"];
            if (string.IsNullOrWhiteSpace(github) || !RepositoryName().IsMatch(github)) return null;
            return new PullRequestTarget(github,
                string.IsNullOrWhiteSpace(section["BaseBranch"]) ? "main" : section["BaseBranch"]!,
                string.IsNullOrWhiteSpace(section["RemoteUrl"]) ? $"https://github.com/{github}.git" : section["RemoteUrl"]!,
                configuration["Coding:GitHubApiBaseUrl"] is { Length: > 0 } api ? api : "https://api.github.com");
        }

        return null;
    }

    private static Dictionary<string, string> GitEnvironment(PullRequestTarget? repository, string? token, string home)
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin",
            ["HOME"] = home,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = "/dev/null"
        };
        foreach (var key in new[] { "SSL_CERT_FILE", "SSL_CERT_DIR", "GIT_SSL_CAINFO", "HTTPS_PROXY", "https_proxy",
                     "NO_PROXY", "no_proxy", "LANG" })
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value)
                env[key] = value;
        if (repository is not null && token is not null &&
            repository.RemoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // The token travels in the environment, never in argv where other processes could read it.
            env["GIT_CONFIG_COUNT"] = "1";
            env["GIT_CONFIG_KEY_0"] = "http.extraheader";
            env["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " +
                                        Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token));
        }

        return env;
    }

    private async Task<string> GitAsync(string workingDirectory, IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken, string purpose)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        start.Environment.Clear();
        foreach (var (key, value) in environment) start.Environment[key] = value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                throw;
            }

            var output = await stdout;
            if (process.ExitCode != 0)
            {
                var error = await stderr;
                logger.LogWarning("git {Command} failed while trying to {Purpose}: {Error}", arguments[0], purpose,
                    RedactCredentials(error.Length > 400 ? error[..400] : error));
                throw new CodingPullRequestException("git_failed", $"Jarvis could not {purpose}.");
            }

            return output;
        }
        catch (Win32Exception)
        {
            throw new CodingPullRequestException("git_missing", "Git is not available on the Jarvis server.");
        }
    }

    private static string RedactCredentials(string text) => Regex.Replace(text, @"(?i)(authorization:\s*)\S+.*", "$1***");

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A unique branch per attempt, so a retry never collides with a branch from an earlier try.</summary>
    public static string BranchName(Guid runId, string title, DateTimeOffset now)
    {
        var slug = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        var id = runId.ToString("N")[^8..] + "-" +
                 now.ToString("MMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(slug) ? $"jarvis/fix-{id}" : $"jarvis/fix-{id}-{slug}";
    }

    private static string CleanTitle(string? title, CodingRunRecord run)
    {
        var candidate = !string.IsNullOrWhiteSpace(title) ? title : TitleFromTask(run.Task);
        candidate = candidate.Trim().Replace("\r", " ").Replace("\n", " ");
        return candidate.Length <= 72 ? candidate : candidate[..71].TrimEnd() + "…";
    }

    /// <summary>The task's own "Change:" line when it has one, otherwise its first non-empty line.</summary>
    public static string TitleFromTask(string task)
    {
        var lines = task.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var change = lines.FirstOrDefault(line => line.StartsWith("Change:", StringComparison.OrdinalIgnoreCase));
        if (change is not null && change.Length > 7) return change[7..].Trim();
        return lines.FirstOrDefault(line => line.Length > 0) ?? "Jarvis change";
    }

    private static string CommitMessage(string title, CodingRunRecord run) =>
        $"{title}\n\nProposed by Jarvis from coding run {run.Id}.\nA person must review and merge this change.";

    public static string BuildBody(CodingRunRecord run, IReadOnlyList<CodingDiffFile> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## What changed");
        builder.AppendLine(string.IsNullOrWhiteSpace(run.Summary) ? "_No summary was produced._" : run.Summary.Trim());
        builder.AppendLine();
        builder.AppendLine("## Why");
        builder.AppendLine(run.Task.Trim());
        builder.AppendLine();
        builder.AppendLine("## Files");
        foreach (var file in files.Take(50))
            builder.AppendLine($"- `{file.Path}` ({file.Status}){(file.Protected ? " — **security-sensitive**" : string.Empty)}");
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine("Opened by Jarvis from a coding run. It was not merged automatically; review it in the Jarvis app or here.");
        var text = builder.ToString();
        return text.Length <= 60_000 ? text : text[..60_000];
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryName();

    private sealed record PullRequestTarget(string GitHubRepository, string BaseBranch, string RemoteUrl, string ApiBaseUrl);

    private sealed class GitHubPullRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("number")] public int Number { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("state")] public string State { get; set; } = "open";
        [System.Text.Json.Serialization.JsonPropertyName("merged")] public bool Merged { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("mergeable")] public bool? Mergeable { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("mergeable_state")] public string? MergeableState { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("head")] public GitHubRef? Head { get; set; }
    }

    private sealed class GitHubRef
    {
        [System.Text.Json.Serialization.JsonPropertyName("sha")] public string? Sha { get; set; }
    }

    private sealed class GitHubCheckRuns
    {
        [System.Text.Json.Serialization.JsonPropertyName("check_runs")] public List<GitHubCheckRun> CheckRuns { get; set; } = [];
    }

    private sealed class GitHubCheckRun
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("status")] public string Status { get; set; } = "queued";
        [System.Text.Json.Serialization.JsonPropertyName("conclusion")] public string? Conclusion { get; set; }
    }
}
