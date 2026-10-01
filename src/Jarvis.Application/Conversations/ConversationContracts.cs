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
public sealed record AgentToolProgress(string ToolCallId, string ToolName, string Phase);
public sealed record AgentStreamEvent(string? TextDelta = null, AgentToolApprovalRequest? ApprovalRequest = null,
    AgentToolProgress? ToolProgress = null);
