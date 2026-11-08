using Jarvis.Agents;

namespace Jarvis.Api.Hosting;

/// <summary>
/// Brings the Codex CLI to <see cref="CodexVersion.Pinned"/> once at startup, in the background so the API is
/// reachable meanwhile. The API and the worker share CODEX_HOME, so enforcing it here covers both.
/// </summary>
internal sealed class CodexVersionGuard(CodexInstallation installation, ILogger<CodexVersionGuard> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var outcome = await installation.EnsurePinnedAsync(stoppingToken);
            logger.LogInformation("Codex CLI version check: {Outcome}.", outcome);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // Chat keeps working on whatever CLI is installed; the next start tries again.
            logger.LogWarning(exception, "Could not bring the Codex CLI to {Pinned}.", CodexVersion.Pinned);
        }
    }
}
