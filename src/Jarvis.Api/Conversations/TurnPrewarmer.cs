using System.Collections.Concurrent;
using Jarvis.Agents;
using Jarvis.Api.Security;
using Jarvis.Mcp;
using Microsoft.Extensions.AI;

namespace Jarvis.Api.Conversations;

/// <summary>
/// Gets the next chat turn's slow parts going while the owner is still typing: a Codex app-server process and the
/// owner's MCP server connections, which both stay pooled for the turn that follows. Asking again within
/// <see cref="MinInterval"/> does nothing, so a client can call it on every keystroke burst.
/// </summary>
public sealed class TurnPrewarmer(
    IServiceScopeFactory scopes,
    IChatClient chatClient,
    IHostApplicationLifetime lifetime,
    ILogger<TurnPrewarmer> logger)
{
    internal static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan WarmTimeout = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastWarmed = new();

    /// <summary>Starts warming for <paramref name="ownerId"/>; false when it already did so a moment ago.</summary>
    public bool Request(Guid ownerId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastWarmed.TryGetValue(ownerId, out var last) && now - last < MinInterval) return false;
        _lastWarmed[ownerId] = now;

        chatClient.GetService<CodexCliChatClient>()?.Prewarm();
        _ = Task.Run(() => WarmIntegrationsAsync(ownerId));
        return true;
    }

    private async Task WarmIntegrationsAsync(Guid ownerId)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            timeout.CancelAfter(WarmTimeout);
            await using var scope = scopes.CreateOwnerScope(ownerId);
            // Connecting puts each server in the shared pool; disposing the scope hands them back, still open.
            await scope.ServiceProvider.GetRequiredService<McpToolHost>().InitializeAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            // A warm-up is only a head start: the turn itself connects whatever is still missing.
            logger.LogDebug(exception, "Warming MCP servers for the next turn did not finish.");
        }
    }
}
