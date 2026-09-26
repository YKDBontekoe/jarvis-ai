namespace Jarvis.Domain.Workflows;

public sealed class JarvisTask
{
    private JarvisTask() { }

    public JarvisTask(Guid ownerId, string title, string prompt)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Title = title.Trim();
        Prompt = prompt.Trim();
        WorkflowId = $"jarvis-task-{Id:N}";
        UserMessageId = Guid.CreateVersion7();
        ResultMessageId = Guid.CreateVersion7();
        Status = "queued";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Prompt { get; private set; } = string.Empty;
    public string Status { get; private set; } = "queued";
    public string WorkflowId { get; private set; } = string.Empty;
    public Guid ConversationId { get; private set; }
    public Guid UserMessageId { get; private set; }
    public Guid ResultMessageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduleDispatchedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Summary { get; private set; }

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public void AttachConversation(Guid conversationId)
    {
        if (ConversationId != Guid.Empty) throw new InvalidOperationException("Task conversation is already set.");
        ConversationId = conversationId;
    }

    public void MarkRunning()
    {
        if (Status is "queued" or "running")
        {
            Status = "running";
            StartedAt ??= DateTimeOffset.UtcNow;
        }
    }

    public void MarkNeedsApproval()
    {
        if (Status is "queued" or "running") Status = "needs_approval";
    }

    public void Complete(string summary)
    {
        if (Status is "completed" or "cancelled") return;
        Status = "completed";
        Summary = summary;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string summary)
    {
        if (Status is "completed" or "cancelled") return;
        Status = "failed";
        Summary = summary;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status is "completed" or "failed" or "cancelled") return;
        Status = "cancelled";
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
