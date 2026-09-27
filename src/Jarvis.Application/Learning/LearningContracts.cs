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
}
