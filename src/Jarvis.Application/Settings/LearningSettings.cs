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
    int DreamingHour = 3)
{
    public static LearningSettings Default { get; } = new();

    public LearningSettings Normalize()
    {
        if (HeartbeatMinutes is < 15 or > 1_440)
            throw new ArgumentException("Choose a heartbeat interval between 15 minutes and 24 hours.", nameof(HeartbeatMinutes));
        if (QuietHoursStart is < 0 or > 23 || QuietHoursEnd is < 0 or > 23)
            throw new ArgumentException("Quiet hours must be whole hours from 0 to 23.", nameof(QuietHoursStart));
        if (DreamingHour is < 0 or > 23)
            throw new ArgumentException("Choose a dreaming hour from 0 to 23.", nameof(DreamingHour));
        return this;
    }

    /// <summary>True when the local hour falls inside quiet hours, which may wrap past midnight.</summary>
    public bool IsQuietHour(int localHour) => QuietHoursStart == QuietHoursEnd
        ? false
        : QuietHoursStart < QuietHoursEnd
            ? localHour >= QuietHoursStart && localHour < QuietHoursEnd
            : localHour >= QuietHoursStart || localHour < QuietHoursEnd;
}
