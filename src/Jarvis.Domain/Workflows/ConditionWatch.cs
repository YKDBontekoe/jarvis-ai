namespace Jarvis.Domain.Workflows;

public sealed class ConditionWatch
{
    private ConditionWatch() { }

    public ConditionWatch(Guid ownerId, string title, string url, string jsonPath, string comparison,
        double threshold, int intervalMinutes, string kind = WatchKinds.PublicJson,
        string? credentialProvider = null, double? latitude = null, double? longitude = null,
        double? radiusMeters = null, int? minutesBefore = null)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Title = title;
        Kind = WatchKinds.Normalize(kind);
        Url = url;
        JsonPath = jsonPath;
        Comparison = comparison;
        Threshold = threshold;
        IntervalMinutes = intervalMinutes;
        CredentialProvider = credentialProvider;
        Latitude = latitude;
        Longitude = longitude;
        RadiusMeters = radiusMeters;
        MinutesBefore = minutesBefore;
        WorkflowId = $"jarvis-watch-{Id:N}";
        Status = "active";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Kind { get; private set; } = WatchKinds.PublicJson;
    public string Url { get; private set; } = string.Empty;
    public string JsonPath { get; private set; } = string.Empty;
    public string Comparison { get; private set; } = string.Empty;
    public double Threshold { get; private set; }
    public int IntervalMinutes { get; private set; }
    public string? CredentialProvider { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? RadiusMeters { get; private set; }
    public int? MinutesBefore { get; private set; }
    public string WorkflowId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "active";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduleDispatchedAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public double? LastValue { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public const int ScheduleStaleGraceMinutes = 30;

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public bool IsScheduleStale(DateTimeOffset utcNow) =>
        Status == "active" && ScheduleDispatchedAt is not null &&
        (LastCheckedAt ?? ScheduleDispatchedAt.Value).AddMinutes(IntervalMinutes + ScheduleStaleGraceMinutes) < utcNow;

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
