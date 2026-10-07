namespace Jarvis.Application.Settings;

/// <summary>Owner controls for continuous learning, self-authored skills, dreaming, and the proactive heartbeat.</summary>
public sealed record LearningSettings(
    bool HeartbeatEnabled = true,
    int HeartbeatMinutes = 60,
    bool LearnPersona = true,
    bool AutoCreateSkills = true,
    bool AutoActivateSkills = false,
    bool ProactiveCheckIns = true,
    int QuietHoursStart = 22,
    int QuietHoursEnd = 7,
    bool DreamingEnabled = true,
    int DreamingHour = 3,
    bool CaptureSignals = true,
    int TraceRetentionDays = 30,
    bool ProposeImprovements = true,
    bool AutoApplyLowRiskMemory = true)
{
    public static LearningSettings Default { get; } = new();

    /// <summary>How long learning signals are kept; they are small and outlive the traces they refer to.</summary>
    public const int SignalRetentionDays = 90;

    public LearningSettings Normalize()
    {
        if (HeartbeatMinutes is < 15 or > 1_440)
            throw new ArgumentException("Choose a heartbeat interval between 15 minutes and 24 hours.", nameof(HeartbeatMinutes));
        if (QuietHoursStart is < 0 or > 23 || QuietHoursEnd is < 0 or > 23)
            throw new ArgumentException("Quiet hours must be whole hours from 0 to 23.", nameof(QuietHoursStart));
        if (DreamingHour is < 0 or > 23)
            throw new ArgumentException("Choose a dreaming hour from 0 to 23.", nameof(DreamingHour));
        if (TraceRetentionDays is < 7 or > 365)
            throw new ArgumentException("Keep run traces for 7 to 365 days.", nameof(TraceRetentionDays));
        return this;
    }

    /// <summary>The cut-off before which run traces are deleted.</summary>
    public DateTimeOffset TraceCutoff(DateTimeOffset now) => now.AddDays(-TraceRetentionDays);

    /// <summary>The cut-off before which learning signals are deleted.</summary>
    public static DateTimeOffset SignalCutoff(DateTimeOffset now) => now.AddDays(-SignalRetentionDays);

    /// <summary>True when the local hour falls inside quiet hours, which may wrap past midnight.</summary>
    public bool IsQuietHour(int localHour) => QuietHoursStart == QuietHoursEnd
        ? false
        : QuietHoursStart < QuietHoursEnd
            ? localHour >= QuietHoursStart && localHour < QuietHoursEnd
            : localHour >= QuietHoursStart || localHour < QuietHoursEnd;
}
