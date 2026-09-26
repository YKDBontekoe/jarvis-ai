namespace Jarvis.Domain.Workflows;

public sealed class Reminder
{
    private Reminder() { }

    public Reminder(Guid ownerId, string title, DateTimeOffset dueAt)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Title = title;
        DueAt = dueAt.ToUniversalTime();
        WorkflowId = $"jarvis-reminder-{Id:N}";
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateTimeOffset DueAt { get; private set; }
    public string WorkflowId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "pending";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduleDispatchedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public void Cancel()
    {
        if (Status != "pending") throw new InvalidOperationException("Only pending reminders can be cancelled.");
        Status = "cancelled";
    }

    public void Complete()
    {
        if (Status != "pending") return;
        Status = "completed";
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void FailScheduling()
    {
        if (Status == "pending") Status = "failed";
    }
}
