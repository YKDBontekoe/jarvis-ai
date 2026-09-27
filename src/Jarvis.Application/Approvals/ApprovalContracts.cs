using Jarvis.Domain.Approvals;

namespace Jarvis.Application.Approvals;

public sealed record ToolApprovalRecord(Guid Id, Guid OwnerId, Guid ConversationId, Guid? TaskId, string RequestId,
    string ToolCallId, string ToolName, string ArgumentsJson, string Status, bool? Approved,
    string ResumeStatus, DateTimeOffset? ResumeStartedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

public sealed record ToolApprovalReply(string RequestId, string ToolCallId, string ToolName,
    string ArgumentsJson, bool Approved);

public sealed record ToolApprovalCreateResult(ToolApprovalRecord Approval, bool Created, Guid? NotificationId);

public interface IToolApprovalStore
{
    Task<ToolApprovalCreateResult> CreateAsync(Guid ownerId, Guid conversationId, string requestId,
        string toolCallId, string toolName, string argumentsJson, Guid? taskId, CancellationToken cancellationToken);
    Task<ToolApprovalRecord?> GetActionableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ToolApprovalRecord>> ListActionableAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ToolApprovalRecord>> ListActionableForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task<bool> HasPendingForTaskAsync(Guid taskId, Guid ownerId, CancellationToken cancellationToken);
    Task<ToolApprovalRecord?> DecideAsync(Guid id, Guid ownerId, bool approved, CancellationToken cancellationToken);
    Task<bool> TryStartResumeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task HeartbeatResumeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task MarkResumeCompletedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task MarkResumeFailedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task CancelIncompleteForTaskAsync(Guid taskId, Guid ownerId, CancellationToken cancellationToken);
}

public static class ToolApprovalMapping
{
    public static ToolApprovalRecord ToRecord(this ToolApproval approval) => new(
        approval.Id, approval.OwnerId, approval.ConversationId, approval.TaskId, approval.RequestId,
        approval.ToolCallId, approval.ToolName, approval.ArgumentsJson, approval.Status,
        approval.Approved, approval.ResumeStatus, approval.ResumeStartedAt, approval.CreatedAt, approval.DecidedAt);

    public static ToolApprovalReply ToReply(this ToolApprovalRecord approval) => new(
        approval.RequestId, approval.ToolCallId, approval.ToolName, approval.ArgumentsJson,
        approval.Approved ?? false);
}
