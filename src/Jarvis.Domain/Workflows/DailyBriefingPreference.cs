namespace Jarvis.Domain.Workflows;

public sealed class DailyBriefingPreference
{
    private DailyBriefingPreference() { }

    public DailyBriefingPreference(Guid ownerId, bool enabled, TimeOnly localTime, string timeZoneId)
    {
        OwnerId = ownerId;
        Enabled = enabled;
        LocalTime = localTime;
        TimeZoneId = timeZoneId;
        WorkflowId = NewWorkflowId(ownerId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid OwnerId { get; private set; }
    public bool Enabled { get; private set; }
    public TimeOnly LocalTime { get; private set; }
    public string TimeZoneId { get; private set; } = "UTC";
    public string WorkflowId { get; private set; } = string.Empty;
    public DateTimeOffset? ScheduleDispatchedAt { get; private set; }
    public DateOnly? LastDeliveredDate { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public string Update(bool enabled, TimeOnly localTime, string timeZoneId)
    {
        var previousWorkflowId = WorkflowId;
        Enabled = enabled;
        LocalTime = localTime;
        TimeZoneId = timeZoneId;
        WorkflowId = enabled ? NewWorkflowId(OwnerId) : previousWorkflowId;
        ScheduleDispatchedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        return previousWorkflowId;
    }

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public bool MarkDelivered(DateOnly localDate)
    {
        if (!Enabled || LastDeliveredDate >= localDate) return false;
        LastDeliveredDate = localDate;
        return true;
    }

    private static string NewWorkflowId(Guid ownerId) => $"jarvis-briefing-{ownerId:N}-{Guid.CreateVersion7():N}";
}
