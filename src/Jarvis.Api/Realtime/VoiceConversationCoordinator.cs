using Jarvis.Api.Conversations;
using Jarvis.Api.Security;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class VoiceConversationCoordinator(
    IServiceScopeFactory scopeFactory,
    IHubContext<JarvisEventsHub> hub)
{
    internal const string ApprovalNeeded = "I need your approval before I can continue. Check the Jarvis app.";
    internal const string ApprovalFinishing =
        "I'm still finishing the last tool decision. Check the Jarvis app if it needs a retry.";

    public async Task<string?> HandleTranscriptAsync(Guid ownerId, Guid conversationId, string transcript,
        CancellationToken cancellationToken, Func<string, CancellationToken, Task>? onTextDelta = null)
    {
        transcript = transcript.Trim();
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        if (transcript.Length > 32_000) transcript = transcript[..32_000];

        await using var scope = scopeFactory.CreateOwnerScope(ownerId);
        var turns = scope.ServiceProvider.GetRequiredService<ConversationTurnService>();
        var result = await turns.SendAsync(ownerId, conversationId, transcript, cancellationToken, onTextDelta,
            beforeRun: token => hub.Clients.Group(JarvisEventsHub.GroupName(conversationId))
                .SendAsync("voice.transcript", new { conversationId, text = transcript }, token));
        return ToSpokenResponse(result);
    }

    internal static string? ToSpokenResponse(ConversationTurnResult result) => result switch
    {
        ConversationTurnResult.Completed completed => completed.Message.Content,
        ConversationTurnResult.AwaitingApproval => ApprovalNeeded,
        ConversationTurnResult.Conflict { Message: var message } when message.StartsWith("Decide", StringComparison.Ordinal) =>
            ApprovalNeeded,
        ConversationTurnResult.Conflict { Message: var message } when message.StartsWith("A previous", StringComparison.Ordinal) =>
            ApprovalFinishing,
        ConversationTurnResult.Failed => "I could not complete that. Check the Jarvis app.",
        _ => null
    };
}
