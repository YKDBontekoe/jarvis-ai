namespace Jarvis.Application.Settings;

/// <summary>
/// How much Jarvis may do on its own between conversations. Stored in the owner settings section
/// <c>autonomy</c>; a missing row means these defaults, and <see cref="Enabled"/> switches every autonomous action off
/// at once. Autonomous work stays inside the normal approval rules: anything that sends, spends, deletes or
/// otherwise reaches outside Jarvis still waits for the owner.
/// </summary>
public sealed record AutonomySettings(
    bool Enabled = true,
    bool HeartbeatMayStartTasks = true,
    int MaxHeartbeatTasksPerDay = 3,
    int MaxHeartbeatTasksPerRun = 1,
    bool TriageInbox = true,
    bool DigestInsteadOfDrop = true)
{
    public const int MaxTasksPerDayLimit = 10;
    public const int MaxTasksPerRunLimit = 3;

    public static AutonomySettings Default { get; } = new();

    public AutonomySettings Normalize()
    {
        if (MaxHeartbeatTasksPerDay is < 0 or > MaxTasksPerDayLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxTasksPerDayLimit} background tasks per day.", nameof(MaxHeartbeatTasksPerDay));
        if (MaxHeartbeatTasksPerRun is < 0 or > MaxTasksPerRunLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxTasksPerRunLimit} background tasks per check.", nameof(MaxHeartbeatTasksPerRun));
        return this;
    }

    /// <summary>True when the heartbeat may start background tasks at all.</summary>
    public bool CanStartHeartbeatTasks =>
        Enabled && HeartbeatMayStartTasks && MaxHeartbeatTasksPerDay > 0 && MaxHeartbeatTasksPerRun > 0;
}
