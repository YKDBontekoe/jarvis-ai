namespace Jarvis.Application.Learning;

public sealed record HeartbeatWorkflowInput(Guid OwnerId);

public sealed record HeartbeatRunResult(bool Continue, int NextRunMinutes);

/// <summary>Bookkeeping between heartbeats so check-ins never repeat the same alert.</summary>
public sealed record HeartbeatState(
    DateTimeOffset? LastRunAt = null,
    DateTimeOffset? LastReflectedMessageAt = null,
    IReadOnlyList<string>? NotifiedKeys = null,
    string? LastSummary = null)
{
    public const int MaxRememberedKeys = 200;

    public IReadOnlyList<string> Keys => NotifiedKeys ?? [];
}

public sealed record ReflectionOutcome(int NewPersonaTraits, int ReinforcedPersonaTraits, int SkillsSaved,
    int MemoriesSaved, int FeedbackProcessed, int MessagesReviewed)
{
    public static ReflectionOutcome Nothing { get; } = new(0, 0, 0, 0, 0, 0);

    public bool LearnedAnything => NewPersonaTraits + ReinforcedPersonaTraits + SkillsSaved + MemoriesSaved > 0;
}

public sealed record CheckInItem(string Key, string Text);

public sealed record HeartbeatOutcome(ReflectionOutcome Reflection, IReadOnlyList<CheckInItem> CheckIns,
    bool QuietHours, string Summary);

public interface IHeartbeatScheduler
{
    Task ScheduleHeartbeatAsync(Guid ownerId, CancellationToken cancellationToken);
    Task CancelHeartbeatAsync(Guid ownerId, CancellationToken cancellationToken);
}

public static class HeartbeatWorkflowIds
{
    public static string For(Guid ownerId) => $"jarvis:heartbeat:{ownerId:N}";
}

public static class LearningSections
{
    public const string HeartbeatState = "heartbeat-state";
    public const string DreamingState = "dreaming-state";
}

public sealed record DreamingWorkflowInput(Guid OwnerId);

public sealed record DreamingRunResult(bool Continue, int NextRunMinutes);

/// <summary>
/// Durable dreaming bookkeeping: recall traces, last sweep, the human-readable diary, and the user portrait
/// that chat appends to the system prompt.
/// </summary>
public sealed record DreamingState(
    DateTimeOffset? LastRunAt = null,
    string? LastPhase = null,
    string? LastSummary = null,
    IReadOnlyList<DreamDiaryEntry>? Diary = null,
    IReadOnlyList<MemoryRecallRecord>? Recalls = null,
    string? UserSummary = null,
    DateTimeOffset? UserSummaryUpdatedAt = null)
{
    public const int MaxDiaryEntries = 20;
    public const int MaxRecallRecords = 200;

    public IReadOnlyList<DreamDiaryEntry> Entries => Diary ?? [];
    public IReadOnlyList<MemoryRecallRecord> RecallList => Recalls ?? [];
}

public sealed record DreamDiaryEntry(DateTimeOffset At, string Phase, string Title, string Body);

/// <summary>How often a stored memory was retrieved in conversation, without storing the query text.</summary>
public sealed record MemoryRecallRecord(Guid MemoryId, int Hits, int UniqueQueries, DateTimeOffset LastHitAt);

public sealed record DreamingOutcome(
    int Staged,
    int Promoted,
    int Merged,
    int Superseded,
    int Deduplicated,
    int PersonaUpdated,
    int FactsMerged,
    bool Skipped,
    string Summary,
    IReadOnlyList<DreamDiaryEntry> Diary,
    bool UserSummaryUpdated = false)
{
    public static DreamingOutcome Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, true, "Nothing to dream about yet.", []);

    public bool ImprovedAnything =>
        Promoted + Merged + Superseded + Deduplicated + PersonaUpdated + FactsMerged > 0 || UserSummaryUpdated;
}

public interface IDreamingScheduler
{
    Task ScheduleDreamingAsync(Guid ownerId, CancellationToken cancellationToken);
    Task CancelDreamingAsync(Guid ownerId, CancellationToken cancellationToken);
}

public static class DreamingWorkflowIds
{
    public static string For(Guid ownerId) => $"jarvis:dreaming:{ownerId:N}";
}

/// <summary>In-process recall signals used by deep ranking. Query text is never stored.</summary>
public interface IMemoryRecallTracker
{
    void Record(Guid ownerId, Guid memoryId, string query);
    IReadOnlyList<MemoryRecallRecord> Snapshot(Guid ownerId);
}

public static class DreamingClock
{
    /// <summary>
    /// Minutes until the next local dreaming hour. Clamped to the heartbeat-style 15 minute–24 hour window
    /// so Temporal delays stay bounded.
    /// </summary>
    public static int MinutesUntilNext(DateTimeOffset utcNow, int hour, string? timeZoneId)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow.ToUniversalTime(), timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var localTime = new TimeOnly(Math.Clamp(hour, 0, 23), 0);
        var candidate = ResolveLocalTime(localDate.ToDateTime(localTime), timeZone);
        if (candidate <= utcNow.ToUniversalTime())
            candidate = ResolveLocalTime(localDate.AddDays(1).ToDateTime(localTime), timeZone);
        var minutes = (int)Math.Ceiling((candidate - utcNow.ToUniversalTime()).TotalMinutes);
        return Math.Clamp(minutes, 15, 1_440);
    }

    private static DateTimeOffset ResolveLocalTime(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var minutes = 0; timeZone.IsInvalidTime(local) && minutes < 180; minutes++)
            local = local.AddMinutes(1);
        if (timeZone.IsInvalidTime(local))
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
