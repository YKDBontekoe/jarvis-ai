namespace Jarvis.Domain.Workflows;

public sealed class Reminder
{
    public const string RecurrenceNone = "none";
    public const string RecurrenceDaily = "daily";
    public const string RecurrenceWeekdays = "weekdays";
    public const string RecurrenceWeekly = "weekly";
    public const int OverdueRescheduleGraceMinutes = 2;
    public const string LocationArrive = "arrive";
    public const string LocationLeave = "leave";
    public const double MinLocationRadiusMeters = 50;
    public const double MaxLocationRadiusMeters = 5_000;
    public const int MaxLocationNameLength = 120;

    /// <summary>Fixes worse than this are too vague to tell inside from outside, so they are ignored.</summary>
    public const double MaxUsableAccuracyMeters = 1_000;

    /// <summary>A repeating place reminder stays quiet this long after it fired, so GPS jitter cannot re-fire it.</summary>
    public static readonly TimeSpan LocationRepeatCooldown = TimeSpan.FromMinutes(30);

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

    public string? LocationName { get; private set; }
    public double? LocationLatitude { get; private set; }
    public double? LocationLongitude { get; private set; }
    public double? LocationRadiusMeters { get; private set; }
    public string? LocationTrigger { get; private set; }
    public bool LocationRepeats { get; private set; }

    /// <summary>Whether the last usable position was inside the place; null until one is known.</summary>
    public bool? LocationInside { get; private set; }

    public bool IsRecurring => Recurrence != RecurrenceNone;
    public bool IsLocationBased => LocationLatitude is not null && LocationLongitude is not null;

    /// <summary>
    /// Creates a reminder that fires when the owner arrives at or leaves a place instead of at a time.
    /// <see cref="DueAt"/> holds the creation moment so lists keep a stable order.
    /// </summary>
    public static Reminder ForPlace(Guid ownerId, string title, string placeName, double latitude, double longitude,
        double radiusMeters, string trigger, bool repeats, string timeZoneId)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180 || !double.IsFinite(latitude) ||
            !double.IsFinite(longitude))
            throw new ArgumentException("The place needs a valid latitude and longitude.", nameof(latitude));
        if (!double.IsFinite(radiusMeters) || radiusMeters is < MinLocationRadiusMeters or > MaxLocationRadiusMeters)
            throw new ArgumentException(
                $"The place radius must be between {MinLocationRadiusMeters:0} and {MaxLocationRadiusMeters:0} meters.",
                nameof(radiusMeters));
        if (trigger is not (LocationArrive or LocationLeave))
            throw new ArgumentException("A place reminder fires on arrive or leave.", nameof(trigger));
        var name = placeName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > MaxLocationNameLength)
            throw new ArgumentException($"The place name must contain 1 to {MaxLocationNameLength} characters.",
                nameof(placeName));

        var reminder = new Reminder(ownerId, title, DateTimeOffset.UtcNow, RecurrenceNone, 0, timeZoneId, null, null)
        {
            LocationName = name,
            LocationLatitude = latitude,
            LocationLongitude = longitude,
            LocationRadiusMeters = radiusMeters,
            LocationTrigger = trigger,
            LocationRepeats = repeats
        };
        return reminder;
    }

    /// <summary>
    /// Feeds one position fix to a pending place reminder. It updates whether the owner is inside and returns true
    /// when this fix crosses the edge the reminder waits for. Fixes in the band just outside the radius change
    /// nothing, so a phone hovering at the edge does not flip back and forth.
    /// </summary>
    public bool ObservePosition(double distanceMeters, double? accuracyMeters, DateTimeOffset at)
    {
        if (!IsLocationBased || Status != "pending" || LocationRadiusMeters is not double radius) return false;
        var accuracy = accuracyMeters is double value && double.IsFinite(value) ? Math.Max(0, value) : 0;
        if (accuracy > MaxUsableAccuracyMeters || !double.IsFinite(distanceMeters)) return false;

        var hysteresis = Math.Max(50, Math.Min(accuracy, 250));
        bool? insideNow = distanceMeters <= radius ? true : distanceMeters > radius + hysteresis ? false : null;
        if (insideNow is null) return false;

        var previous = LocationInside;
        LocationInside = insideNow;
        var crossed = LocationTrigger == LocationLeave
            ? insideNow == false && previous == true
            : insideNow == true && previous != true;
        if (!crossed) return false;
        return LastDeliveredAt is not { } last || at - last >= LocationRepeatCooldown;
    }

    /// <summary>Seeds the inside state from a recent fix so "when I get home" does not fire while already home.</summary>
    public void SeedLocationState(double distanceMeters, double? accuracyMeters)
    {
        if (!IsLocationBased || LocationRadiusMeters is not double radius) return;
        if (accuracyMeters is > MaxUsableAccuracyMeters || !double.IsFinite(distanceMeters)) return;
        LocationInside = distanceMeters <= radius;
    }

    /// <summary>Records that the place reminder fired. One-time ones complete; repeating ones wait for the next visit.</summary>
    public void CompleteLocationOccurrence(DateTimeOffset deliveredAt)
    {
        if (Status != "pending" || !IsLocationBased) return;
        LastDeliveredAt = deliveredAt;
        if (!LocationRepeats) Complete();
    }

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

    /// <summary>The owner finished it before it fired. Only one-time reminders can be marked done.</summary>
    public void MarkDone()
    {
        if (IsRecurring || LocationRepeats)
            throw new InvalidOperationException("Repeating reminders cannot be marked done; cancel them instead.");
        if (Status != "pending") throw new InvalidOperationException("Only upcoming reminders can be marked done.");
        Complete();
    }

    /// <summary>
    /// Moves a one-time reminder to a later moment, either before it fires or after it was delivered. It gets a
    /// fresh workflow id because the previous workflow may still be closing. A one-time place reminder that is
    /// snoozed becomes a timed reminder, because "later" is a moment, not a place.
    /// </summary>
    public void Snooze(DateTimeOffset dueAt, TimeOnly localTime)
    {
        if (LocationRepeats) throw new InvalidOperationException("Repeating reminders are snoozed as a one-time copy.");
        if (IsRecurring) throw new InvalidOperationException("Repeating reminders are snoozed as a one-time copy.");
        if (Status is not ("pending" or "completed"))
            throw new InvalidOperationException("Only upcoming or delivered reminders can be snoozed.");
        Status = "pending";
        DueAt = dueAt.ToUniversalTime();
        LocalTime = localTime;
        CompletedAt = null;
        ScheduleDispatchedAt = null;
        WorkflowId = $"jarvis-reminder-{Id:N}-{DueAt:yyyyMMddHHmmss}";
        LocationName = null;
        LocationLatitude = null;
        LocationLongitude = null;
        LocationRadiusMeters = null;
        LocationTrigger = null;
        LocationInside = null;
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
