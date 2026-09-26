namespace Jarvis.Domain.Approvals;

public sealed class ToolApproval
{
    private ToolApproval() { }

    public ToolApproval(Guid ownerId, Guid conversationId, string requestId, string toolCallId,
        string toolName, string argumentsJson, Guid? taskId = null)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        ConversationId = conversationId;
        TaskId = taskId;
        RequestId = requestId;
        ToolCallId = toolCallId;
        ToolName = toolName;
        ArgumentsJson = argumentsJson;
        Status = "pending";
        ResumeStatus = "not_started";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid? TaskId { get; private set; }
    public string RequestId { get; private set; } = string.Empty;
    public string ToolCallId { get; private set; } = string.Empty;
    public string ToolName { get; private set; } = string.Empty;
    public string ArgumentsJson { get; private set; } = "{}";
    public string Status { get; private set; } = "pending";
    public string ResumeStatus { get; private set; } = "not_started";
    public DateTimeOffset? ResumeStartedAt { get; private set; }
    public bool? Approved { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    public void Decide(bool approved)
    {
        if (Status != "pending") throw new InvalidOperationException("This tool approval has already been decided.");
        Status = approved ? "approved" : "rejected";
        ResumeStatus = "pending";
        Approved = approved;
        DecidedAt = DateTimeOffset.UtcNow;
    }
}
