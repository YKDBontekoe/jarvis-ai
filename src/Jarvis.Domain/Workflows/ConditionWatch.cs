namespace Jarvis.Domain.Workflows;

public sealed class ConditionWatch
{
    private ConditionWatch() { }

    public ConditionWatch(Guid ownerId, string title, string url, string jsonPath, string comparison,
        double threshold, int intervalMinutes)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Title = title;
        Url = url;
        JsonPath = jsonPath;
        Comparison = comparison;
        Threshold = threshold;
        IntervalMinutes = intervalMinutes;
        WorkflowId = $"jarvis-watch-{Id:N}";
        Status = "active";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string JsonPath { get; private set; } = string.Empty;
    public string Comparison { get; private set; } = string.Empty;
    public double Threshold { get; private set; }
    public int IntervalMinutes { get; private set; }
    public string WorkflowId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "active";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduleDispatchedAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public double? LastValue { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public bool RecordCheck(double value, DateTimeOffset checkedAt)
    {
        if (Status != "active") return false;
        LastValue = value;
        LastCheckedAt = checkedAt;
        var matched = Comparison switch
        {
            "below" => value <= Threshold,
            "above" => value >= Threshold,
            _ => false
        };
        if (matched)
        {
            Status = "triggered";
            CompletedAt = checkedAt;
        }
        return matched;
    }

    public void Cancel()
    {
        if (Status != "active") throw new InvalidOperationException("Only active watches can be cancelled.");
        Status = "cancelled";
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        if (Status != "active") return;
        Status = "failed";
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
