using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class VoiceConversationCoordinator(
    IServiceScopeFactory scopeFactory,
    IHubContext<JarvisEventsHub> hub,
    ILogger<VoiceConversationCoordinator> logger)
{
    public async Task<string?> HandleTranscriptAsync(Guid ownerId, Guid conversationId, string transcript,
        CancellationToken cancellationToken, Func<string, CancellationToken, Task>? onTextDelta = null)
    {
        transcript = transcript.Trim();
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        if (transcript.Length > 32_000) transcript = transcript[..32_000];

        var clients = hub.Clients.Group(JarvisEventsHub.GroupName(conversationId));
        await clients.SendAsync("voice.transcript", new { conversationId, text = transcript }, cancellationToken);
        string? spokenResponse = null;

        var accessorScope = scopeFactory.CreateAsyncScope();
        await using (accessorScope)
        {
            var accessor = accessorScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var priorContext = accessor.HttpContext;
            var internalContext = new DefaultHttpContext();
            internalContext.Items["Jarvis.InternalVoiceOwnerId"] = ownerId;
            accessor.HttpContext = internalContext;
            try
            {
                var services = accessorScope.ServiceProvider;
                var conversations = services.GetRequiredService<IConversationStore>();
                var tasks = services.GetRequiredService<IJarvisTaskRepository>();
                if (await conversations.GetAsync(conversationId, ownerId, cancellationToken) is null ||
                    await tasks.GetTaskByConversationIdAsync(conversationId, ownerId, cancellationToken) is not null)
                    return null;

                var runLock = services.GetRequiredService<IConversationRunLock>();
                await using var lease = await runLock.AcquireAsync(conversationId, cancellationToken);
                var userMessage = new Message(conversationId, "user", transcript);
                await conversations.AddMessageAsync(userMessage, cancellationToken);

                try
                {
                    var coordinator = services.GetRequiredService<AgentRunCoordinator>();
                    var agent = services.GetRequiredService<IJarvisAgent>();
                    var outcome = await coordinator.RunAsync(ownerId, conversationId,
                        agent.StreamReplyAsync(conversationId, userMessage, cancellationToken),
                        transcript, cancellationToken, memorySourceId: userMessage.Id,
                        onTextDelta: onTextDelta);
                    spokenResponse = outcome.PendingApprovals.Count > 0
                        ? "I need your approval before I can continue. Check the Jarvis app."
                        : outcome.AssistantMessage?.Content;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Voice agent run failed for conversation {ConversationId}.", conversationId);
                    await clients.SendAsync("agent.failed", new
                    {
                        conversationId,
                        message = "Jarvis could not complete this response."
                    }, CancellationToken.None);
                }
            }
            finally { accessor.HttpContext = priorContext; }
        }
        return spokenResponse;
    }
}
