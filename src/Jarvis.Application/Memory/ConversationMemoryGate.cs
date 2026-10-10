using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;

namespace Jarvis.Application.Memory;

/// <summary>
/// Extracts memories from a conversation turn the way the conversation's assistant profile allows: nothing when the
/// profile does not remember or does not contribute to learning, and the profile's id on anything it does store. Chat,
/// voice and background tasks all go through this so the rule cannot drift between them.
/// </summary>
public interface IConversationMemoryGate
{
    Task ExtractAsync(Guid ownerId, Guid conversationId, Guid sourceMessageId, string userMessage,
        CancellationToken cancellationToken);
}

public sealed class ConversationMemoryGate(
    IConversationStore conversations,
    IAssistantProfileService profiles,
    IConversationMemoryExtractor extractor) : IConversationMemoryGate
{
    public async Task ExtractAsync(Guid ownerId, Guid conversationId, Guid sourceMessageId, string userMessage,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, ownerId, cancellationToken);
        ProfileBinding? binding = conversation?.ProfileId is { } profileId
            ? new ProfileBinding(profileId, conversation.ProfileVersion ?? 0,
                conversation.ProfileSnapshotJson ?? "", conversation.Title)
            : null;
        var snapshot = await profiles.ResolveSnapshotAsync(binding, ownerId, cancellationToken);
        if (!ProfileScope.AllowsRemember(snapshot) || !ProfileScope.ContributesToLearning(snapshot)) return;
        var context = conversation is null
            ? []
            : await LoadContextAsync(conversationId, sourceMessageId, cancellationToken);
        await extractor.ExtractAndStoreAsync(ownerId, sourceMessageId, userMessage, cancellationToken,
            snapshot.ProfileId, context);
    }

    private const int ContextTurns = 4;
    private const int MaxTurnLength = 1_000;

    /// <summary>
    /// The user and assistant turns just before the source message, so a short reply such as "no, Thursdays now" can be
    /// understood. Long turns keep their end, where the question being answered usually is.
    /// </summary>
    private async Task<IReadOnlyList<MemoryExtractionTurn>> LoadContextAsync(Guid conversationId,
        Guid sourceMessageId, CancellationToken cancellationToken)
    {
        // Replies and approval cards can follow the source message, so read a few more than needed.
        var page = await conversations.GetMessagePageAsync(conversationId, null, ContextTurns * 3, cancellationToken);
        var index = page.Items.ToList().FindIndex(message => message.Id == sourceMessageId);
        if (index <= 0) return [];
        return page.Items.Take(index)
            .Where(message => message.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(ContextTurns)
            .Select(message => new MemoryExtractionTurn(message.Role, Tail(message.Content.Trim())))
            .ToArray();
    }

    private static string Tail(string text) =>
        text.Length <= MaxTurnLength ? text : "…" + text[^MaxTurnLength..];
}
