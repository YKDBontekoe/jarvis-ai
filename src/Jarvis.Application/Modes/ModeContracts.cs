using Jarvis.Application.Integrations;

namespace Jarvis.Application.Modes;

public static class ModeIds
{
    public const string Normal = "normal";
    public const string Focus = "focus";
    public const string Commuting = "commuting";
    public const string Meeting = "meeting";
    public const string Sleep = "sleep";
    public const string Travel = "travel";
    public const string Weekend = "weekend";

    public static readonly IReadOnlyList<string> All =
        [Normal, Focus, Commuting, Meeting, Sleep, Travel, Weekend];

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? mode) =>
        mode is not null && All.Contains(mode);
}

public static class NotificationLevels
{
    public const string All = "all";
    public const string Important = "important";
    public const string None = "none";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? level) =>
        level is All or Important or None;

    /// <summary>What still gets through in "important": things that cannot wait or need a decision.</summary>
    private static readonly HashSet<string> ImportantTypes = new(StringComparer.Ordinal)
    {
        "reminder.due", "reminder.failed", "whatsapp.reminder", "approval.required", "automation.approval",
        "watch.triggered"
    };

    public static bool Allows(string level, string notificationType) => level switch
    {
        None => false,
        Important => ImportantTypes.Contains(notificationType),
        _ => true
    };
}

/// <summary>How Jarvis behaves in a mode: which pushes reach the phone, and an optional tone for chat replies.</summary>
public sealed record ModePolicy(string Notifications = NotificationLevels.All, string? Tone = null);

public sealed record ModeDefinition(string Id, string Label, string Description, ModePolicy Policy);

public static class ModeCatalog
{
    public const int MaxToneLength = 200;

    public static readonly IReadOnlyList<ModeDefinition> Defaults =
    [
        new(ModeIds.Normal, "Normal", "Everything as usual.", new ModePolicy()),
        new(ModeIds.Focus, "Focus", "Only reminders and approvals buzz your phone. Short, to-the-point replies.",
            new ModePolicy(NotificationLevels.Important, "Keep replies short and to the point. Do not suggest new tasks or side topics.")),
        new(ModeIds.Commuting, "Commuting", "Short replies that work when read aloud or at a glance.",
            new ModePolicy(NotificationLevels.Important, "Keep replies very short and easy to listen to. Avoid tables, long lists and links.")),
        new(ModeIds.Meeting, "Meeting", "No pushes while you are in a meeting.",
            new ModePolicy(NotificationLevels.None, "Be brief; the user may be glancing at the phone in a meeting.")),
        new(ModeIds.Sleep, "Sleep", "Do not disturb. Nothing buzzes your phone.",
            new ModePolicy(NotificationLevels.None, "Be calm and brief; the user is winding down or just woke up.")),
        new(ModeIds.Travel, "Travel", "Everything on, with practical answers for being on the road.",
            new ModePolicy(NotificationLevels.All, "Favour practical, time-and-place aware answers; the user is travelling.")),
        new(ModeIds.Weekend, "Weekend", "Relaxed: work-style pushes stay quiet.",
            new ModePolicy(NotificationLevels.Important, "Keep a relaxed tone and do not push work topics.")),
    ];

    public static ModeDefinition Find(string id) => Defaults.First(x => x.Id == id);

    public static string? CleanTone(string? tone)
    {
        if (string.IsNullOrWhiteSpace(tone)) return null;
        var clean = string.Join(' ', tone.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= MaxToneLength ? clean : clean[..MaxToneLength].TrimEnd();
    }
}

public static class ModeSources
{
    public const string Manual = "manual";
    public const string Auto = "auto";
    public const string Default = "default";
}

/// <summary>A mode the owner switched on by hand, until <see cref="Until"/> when set.</summary>
public sealed record ActiveModeState(string Mode, DateTimeOffset SetAt, DateTimeOffset? Until);

/// <summary>
/// Everything stored per owner. <see cref="Auto"/> lets Jarvis switch modes from the clock and calendar;
/// <see cref="Overrides"/> replaces a mode's default policy.
/// </summary>
public sealed record ModeSettings(
    bool Auto = true,
    string SleepStart = "23:00",
    string SleepEnd = "07:00",
    Dictionary<string, ModePolicy>? Overrides = null,
    ActiveModeState? Manual = null)
{
    public static readonly ModeSettings Default = new();
}

public sealed record ModeSignals(DateTimeOffset Now, TimeZoneInfo Zone, CalendarEventRecord? CurrentEvent);

public sealed record ModeDecision(string Mode, string Source, string Reason, DateTimeOffset? Until);

public sealed record EffectiveMode(ModeDefinition Definition, bool Customised);

public sealed record ModeState(
    ModeDecision Current,
    ModePolicy Policy,
    IReadOnlyList<EffectiveMode> Modes,
    bool Auto,
    string SleepStart,
    string SleepEnd);

public interface IModeService
{
    Task<ModeState> GetStateAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Switches a mode on by hand; <c>null</c> or "auto" clears it so Jarvis decides again.</summary>
    Task<ModeOperation<ModeState>> SetModeAsync(Guid ownerId, string? mode, int? minutes,
        CancellationToken cancellationToken);

    Task<ModeOperation<ModeState>> SaveSettingsAsync(Guid ownerId, bool? auto, string? sleepStart, string? sleepEnd,
        CancellationToken cancellationToken);

    Task<ModeOperation<ModeState>> SavePolicyAsync(Guid ownerId, string mode, string? notifications, string? tone,
        CancellationToken cancellationToken);

    Task<ModeOperation<ModeState>> ResetPolicyAsync(Guid ownerId, string mode, CancellationToken cancellationToken);

    /// <summary>False when the current mode keeps this kind of notification off the owner's phone.</summary>
    Task<bool> ShouldPushAsync(Guid ownerId, string notificationType, CancellationToken cancellationToken);
}

public enum ModeFailure
{
    None,
    Invalid
}

public sealed record ModeOperation<T>(T? Value, ModeFailure Failure = ModeFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == ModeFailure.None;

    public static ModeOperation<T> Ok(T value) => new(value);
    public static ModeOperation<T> Invalid(string field, string message) =>
        new(default, ModeFailure.Invalid, field, message);
}
