using System.Globalization;

namespace Jarvis.Application.Modes;

/// <summary>
/// Decides the mode from a manual choice first, then the clock and calendar: sleeping hours, a meeting in
/// progress, the weekend. Everything else is normal. It is plain rules so the owner can predict it.
/// </summary>
public static class ModeInference
{
    public static readonly TimeOnly DefaultSleepStart = new(23, 0);
    public static readonly TimeOnly DefaultSleepEnd = new(7, 0);

    /// <summary>An event with no end time counts as an hour; one longer than this is a day-long marker, not a meeting.</summary>
    public static readonly TimeSpan MaxMeeting = TimeSpan.FromHours(6);

    public static ModeDecision Decide(ModeSettings settings, ModeSignals signals)
    {
        var manual = settings.Manual;
        if (manual is not null && ModeIds.IsValid(manual.Mode) && (manual.Until is null || manual.Until > signals.Now))
        {
            var label = ModeCatalog.Find(manual.Mode).Label;
            return new ModeDecision(manual.Mode, ModeSources.Manual,
                manual.Until is { } until
                    ? $"You switched on {label} until {TimeZoneInfo.ConvertTime(until, signals.Zone):HH:mm}."
                    : $"You switched on {label}.",
                manual.Until);
        }

        if (!settings.Auto)
            return new ModeDecision(ModeIds.Normal, ModeSources.Default, "Automatic modes are off.", null);

        var local = TimeZoneInfo.ConvertTime(signals.Now, signals.Zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var start = ParseTime(settings.SleepStart) ?? DefaultSleepStart;
        var end = ParseTime(settings.SleepEnd) ?? DefaultSleepEnd;
        if (InWindow(time, start, end))
            return new ModeDecision(ModeIds.Sleep, ModeSources.Auto,
                $"It is between {start:HH:mm} and {end:HH:mm}.", NextBoundary(local, end));

        if (signals.CurrentEvent is { } meeting && IsMeeting(meeting, signals.Now))
            return new ModeDecision(ModeIds.Meeting, ModeSources.Auto, $"“{meeting.Title}” is on your calendar now.",
                meeting.EndAt ?? meeting.StartAt.AddHours(1));

        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return new ModeDecision(ModeIds.Weekend, ModeSources.Auto, "It is the weekend.", null);

        return new ModeDecision(ModeIds.Normal, ModeSources.Default, "Nothing special is going on.", null);
    }

    public static bool IsMeeting(Integrations.CalendarEventRecord item, DateTimeOffset now)
    {
        var end = item.EndAt ?? item.StartAt.AddHours(1);
        return item.StartAt <= now && now < end && end - item.StartAt <= MaxMeeting;
    }

    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    /// <summary>True when <paramref name="time"/> is inside the window, which may wrap past midnight.</summary>
    public static bool InWindow(TimeOnly time, TimeOnly start, TimeOnly end) =>
        start == end ? false : start < end ? time >= start && time < end : time >= start || time < end;

    private static DateTimeOffset NextBoundary(DateTimeOffset local, TimeOnly end)
    {
        var candidate = new DateTimeOffset(local.Date.Add(end.ToTimeSpan()), local.Offset);
        return candidate <= local ? candidate.AddDays(1) : candidate;
    }
}
