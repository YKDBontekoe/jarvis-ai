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
        await extractor.ExtractAndStoreAsync(ownerId, sourceMessageId, userMessage, cancellationToken,
            snapshot.ProfileId);
    }
}
