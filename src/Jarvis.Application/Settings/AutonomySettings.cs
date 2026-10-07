namespace Jarvis.Application.Settings;

/// <summary>How far Jarvis may go on its own, as chosen by the owner.</summary>
public static class AutonomyLevels
{
    /// <summary>Read-only tools run without asking, and so do reversible changes inside background tasks.</summary>
    public const string Full = "full";

    /// <summary>Read-only tools run without asking; everything that changes something still asks.</summary>
    public const string Standard = "standard";

    /// <summary>Nothing is approved automatically except what the owner granted per category.</summary>
    public const string AskEverything = "ask_everything";

    public static bool IsValid(string? level) => level is Full or Standard or AskEverything;
}

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
    bool DigestInsteadOfDrop = true,
    string Level = AutonomyLevels.Full,
    bool AutoApproveReadOnly = true,
    bool AutoApproveMcpReadHints = true,
    int MaxAutoApprovalsPerDay = 200)
{
    public const int MaxTasksPerDayLimit = 10;
    public const int MaxTasksPerRunLimit = 3;
    public const int MaxAutoApprovalsLimit = 2_000;

    public static AutonomySettings Default { get; } = new();

    public AutonomySettings Normalize()
    {
        if (MaxHeartbeatTasksPerDay is < 0 or > MaxTasksPerDayLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxTasksPerDayLimit} background tasks per day.", nameof(MaxHeartbeatTasksPerDay));
        if (MaxHeartbeatTasksPerRun is < 0 or > MaxTasksPerRunLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxTasksPerRunLimit} background tasks per check.", nameof(MaxHeartbeatTasksPerRun));
        if (!AutonomyLevels.IsValid(Level))
            throw new ArgumentException("Choose full, standard or ask_everything.", nameof(Level));
        if (MaxAutoApprovalsPerDay is < 0 or > MaxAutoApprovalsLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxAutoApprovalsLimit} automatic approvals per day.",
                nameof(MaxAutoApprovalsPerDay));
        return this;
    }

    /// <summary>True when the heartbeat may start background tasks at all.</summary>
    public bool CanStartHeartbeatTasks =>
        Enabled && HeartbeatMayStartTasks && MaxHeartbeatTasksPerDay > 0 && MaxHeartbeatTasksPerRun > 0;
}
