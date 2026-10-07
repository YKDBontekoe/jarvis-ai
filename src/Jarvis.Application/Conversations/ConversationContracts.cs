using Jarvis.Domain.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Application.Profiles;

namespace Jarvis.Application.Conversations;

public interface IConversationStore
{
    Task<Conversation> CreateAsync(Guid ownerId, string title, CancellationToken cancellationToken,
        ProfileBinding? profile = null);
    Task BindProfileAsync(Guid conversationId, Guid ownerId, ProfileBinding profile,
        CancellationToken cancellationToken);
    Task<Conversation?> GetAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Conversation>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    /// <summary>Renames and/or pins an owner's conversation. Null arguments leave that field unchanged.</summary>
    Task<Conversation?> UpdateAsync(Guid conversationId, Guid ownerId, string? title, bool? pinned,
        CancellationToken cancellationToken);
    Task<ConversationDeleteResult> DeleteAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<MessagePage> GetMessagePageAsync(Guid conversationId, MessageCursor? before, int limit,
        CancellationToken cancellationToken);
    Task AddMessageAsync(Message message, CancellationToken cancellationToken);
    /// <summary>Removes one message of a conversation, with its feedback; true when it existed.</summary>
    Task<bool> DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken);
    Task<string?> GetAgentSessionAsync(Guid conversationId, CancellationToken cancellationToken);
    Task SaveAgentSessionAsync(Guid conversationId, string state, CancellationToken cancellationToken);
}

public sealed record MessageCursor(DateTimeOffset CreatedAt, Guid Id);

public sealed record MessagePage(IReadOnlyList<Message> Items, MessageCursor? NextCursor, bool HasMore)
{
    public const int MaximumSize = 100;
}

/// <summary>Owner-wide, read-only history used by background learning.</summary>
public interface IConversationHistory
{
    Task<IReadOnlyList<Message>> ListRecentMessagesAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// What the assistant profile of each message's conversation allows when Jarvis learns from it. Messages the owner
    /// does not own, or that do not exist, are left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MessageLearningScope>> GetLearningScopesAsync(Guid ownerId,
        IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
}

/// <summary>
/// The learning rules of the profile a message was written under. <see cref="ProfileId"/> is null for the default
/// assistant, whose memories are not tied to a profile.
/// </summary>
public sealed record MessageLearningScope(Guid? ProfileId, bool AllowRemember = true, bool AllowPersonaLearning = true)
{
    public static MessageLearningScope Default { get; } = new((Guid?)null);
}

/// <summary>
/// The combined rules for a batch of messages that a model reads together. Memories may only be stored from a batch
/// that is entirely one profile, because the model's output cannot be traced back to a single message; otherwise a
/// fact from one profile would end up visible to every other.
/// </summary>
public sealed record LearningScope(bool CanStoreMemories, Guid? ProfileId, bool AllowsPersona)
{
    public static LearningScope Of(IEnumerable<MessageLearningScope> scopes)
    {
        var list = scopes.ToArray();
        if (list.Length == 0) return new LearningScope(true, null, true);
        var profiles = list.Select(scope => scope.ProfileId).Distinct().ToArray();
        return new LearningScope(profiles.Length == 1 && list.All(scope => scope.AllowRemember), profiles[0],
            list.All(scope => scope.AllowPersonaLearning));
    }
}

public enum ConversationDeleteResult
{
    Deleted,
    NotFound,
    TaskBacked
}

public interface IConversationRunLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(Guid conversationId, CancellationToken cancellationToken);
}

public interface ICurrentUser
{
    Guid OwnerId { get; }
}

public interface IJarvisAgent
{
    IAsyncEnumerable<AgentStreamEvent> StreamReplyAsync(
        Guid conversationId,
        Message currentUserMessage,
        CancellationToken cancellationToken);

    IAsyncEnumerable<AgentStreamEvent> ResumeReplyAsync(
        Guid conversationId,
        ToolApprovalReply approval,
        CancellationToken cancellationToken);
}

public sealed record AgentToolApprovalRequest(string RequestId, string ToolCallId, string ToolName, string ArgumentsJson);
/// <summary>
/// <paramref name="ErrorKind"/> names why a tool failed (<c>input</c>, <c>transient</c>, <c>failed</c>) and is only
/// set with the <c>failed</c> phase. It is a category, never exception text.
/// </summary>
public sealed record AgentToolProgress(string ToolCallId, string ToolName, string Phase, string? ErrorKind = null);
public sealed record AgentStreamEvent(string? TextDelta = null, AgentToolApprovalRequest? ApprovalRequest = null,
    AgentToolProgress? ToolProgress = null);
