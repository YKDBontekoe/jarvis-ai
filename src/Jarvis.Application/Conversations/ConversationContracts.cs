using Jarvis.Domain.Conversations;
using Jarvis.Application.Approvals;

namespace Jarvis.Application.Conversations;

public interface IConversationStore
{
    Task<Conversation> CreateAsync(Guid ownerId, string title, CancellationToken cancellationToken);
    Task<Conversation?> GetAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Conversation>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ConversationDeleteResult> DeleteAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken);
    Task AddMessageAsync(Message message, CancellationToken cancellationToken);
    Task<string?> GetAgentSessionAsync(Guid conversationId, CancellationToken cancellationToken);
    Task SaveAgentSessionAsync(Guid conversationId, string state, CancellationToken cancellationToken);
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
