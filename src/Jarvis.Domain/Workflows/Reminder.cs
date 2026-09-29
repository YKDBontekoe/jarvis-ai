namespace Jarvis.Domain.Workflows;

public sealed class Reminder
{
    public const string RecurrenceNone = "none";
    public const string RecurrenceDaily = "daily";
    public const string RecurrenceWeekdays = "weekdays";
    public const string RecurrenceWeekly = "weekly";
    public const int OverdueRescheduleGraceMinutes = 2;

    public static readonly IReadOnlySet<string> RecurrenceKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        RecurrenceNone, RecurrenceDaily, RecurrenceWeekdays, RecurrenceWeekly
    };

    private Reminder() { }

    public Reminder(Guid ownerId, string title, DateTimeOffset dueAt)
        : this(ownerId, title, dueAt, RecurrenceNone, 0, "UTC", null, null)
    {
    }

    public Reminder(Guid ownerId, string title, DateTimeOffset dueAt, string recurrence, int weekdays,
        string timeZoneId, TimeOnly? localTime, DateOnly? until)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Title = title;
        DueAt = dueAt.ToUniversalTime();
        WorkflowId = $"jarvis-reminder-{Id:N}";
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        Recurrence = string.IsNullOrWhiteSpace(recurrence) ? RecurrenceNone : recurrence;
        Weekdays = weekdays;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId;
        LocalTime = localTime;
        Until = until;
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
    public string Recurrence { get; private set; } = RecurrenceNone;
    public int Weekdays { get; private set; }
    public string TimeZoneId { get; private set; } = "UTC";
    public TimeOnly? LocalTime { get; private set; }
    public DateOnly? Until { get; private set; }
    public DateTimeOffset? LastDeliveredAt { get; private set; }
    public Guid? ConversationId { get; private set; }

    public bool IsRecurring => Recurrence != RecurrenceNone;

    public void AttachConversation(Guid conversationId)
    {
        if (conversationId == Guid.Empty)
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));
        ConversationId = conversationId;
    }

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public bool IsOverdueDispatchStale(DateTimeOffset utcNow) =>
        Status == "pending" && ScheduleDispatchedAt is not null &&
        DueAt.AddMinutes(OverdueRescheduleGraceMinutes) < utcNow;

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

    /// <summary>
    /// Records a delivered occurrence. One-shot reminders complete. Recurring reminders keep
    /// <see cref="Status"/> pending and move <see cref="DueAt"/> to the next fire, or complete when
    /// there is no next fire.
    /// </summary>
    public void CompleteOccurrence(DateTimeOffset deliveredAt, DateTimeOffset? nextDueAt)
    {
        if (Status != "pending") return;
        LastDeliveredAt = deliveredAt;
        if (nextDueAt is null)
        {
            Complete();
            return;
        }

        DueAt = nextDueAt.Value.ToUniversalTime();
    }
}
