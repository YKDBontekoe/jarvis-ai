using System.Globalization;
using System.Text.RegularExpressions;
using Jarvis.Domain.Workflows;

namespace Jarvis.Application.Workflows;

/// <summary>What a typed reminder command asks for, before it is turned into a moment.</summary>
public abstract record ReminderCommand
{
    /// <summary>In a number of minutes from now.</summary>
    public sealed record After(TimeSpan Delay, string Title) : ReminderCommand;

    /// <summary>At a local clock time, today if it is still ahead, else tomorrow; or on the next day when <paramref name="Tomorrow"/>.</summary>
    public sealed record At(TimeOnly Time, bool Tomorrow, string Title) : ReminderCommand;

    /// <summary>A repeating reminder: every day, every weekday, or on one weekday every week or every other week.</summary>
    public sealed record Every(string Recurrence, int Weekdays, TimeOnly Time, string Title) : ReminderCommand;

    /// <summary>Brings the reminder that just went off back after a delay.</summary>
    public sealed record Snooze(TimeSpan Delay) : ReminderCommand;

    public sealed record List : ReminderCommand;

    public sealed record Usage(string Message) : ReminderCommand;
}

/// <summary>
/// Quick reminders for chat apps that need no model: <c>/remind in 30m call mum</c>, <c>/remind at 18:30 pick up
/// the kids</c>, <c>/remind tomorrow 9:00 dentist</c> and <c>/reminders</c>. Anything else is not a reminder command.
/// </summary>
public static partial class ReminderCommands
{
    public const string UsageText =
        "Reminders: /remind in 30m call mum, /remind at 18:30 pick up the kids, /remind tomorrow 9:00 dentist, " +
        "/remind every monday 9:00 bins, /remind every other friday 17:00 report, /remind every day 8:00 pills, " +
        "/snooze 15m brings the one that just went off back, /reminders shows what is coming. Use m, h or d after the number.";

    public const int MaxTitleLength = 200;
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(366);
    public static readonly TimeOnly DefaultMorning = new(9, 0);

    public static ReminderCommand? Parse(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith('/')) return null;
        var space = trimmed.IndexOfAny([' ', '\n']);
        var command = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();
        if (command == "/reminders") return new ReminderCommand.List();
        if (command == "/snooze")
        {
            if (rest.Length == 0) return new ReminderCommand.Snooze(TimeSpan.FromMinutes(10));
            var delay = SnoozePattern().Match(rest);
            if (!delay.Success) return new ReminderCommand.Usage(UsageText);
            var amount = int.Parse(delay.Groups["n"].Value, CultureInfo.InvariantCulture);
            var span = delay.Groups["u"].Value.ToLowerInvariant() switch
            {
                "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(amount),
                "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
            return amount <= 0 || span > TimeSpan.FromDays(1) ? new ReminderCommand.Usage(UsageText) : new ReminderCommand.Snooze(span);
        }
        if (command != "/remind") return null;

        if (EveryPattern().Match(rest) is { Success: true } every)
        {
            var everyTitle = CleanTitle(every.Groups["t"].Value);
            var everyTime = every.Groups["h"].Success
                ? ParseTime(every.Groups["h"].Value, every.Groups["m"].Value)
                : DefaultMorning;
            var what = every.Groups["w"].Value.ToLowerInvariant();
            var other = every.Groups["o"].Success;
            if (everyTitle is null || everyTime is null) return new ReminderCommand.Usage(UsageText);
            (string Recurrence, int Weekdays)? rule = what switch
            {
                "day" or "days" or "daily" => other ? null : (Reminder.RecurrenceDaily, 0),
                "weekday" or "weekdays" => other ? null : (Reminder.RecurrenceWeekdays, 0),
                _ => WeekdayBit(what) is { } bit
                    ? (other ? Reminder.RecurrenceBiweekly : Reminder.RecurrenceWeekly, bit)
                    : null,
            };
            return rule is { } known
                ? new ReminderCommand.Every(known.Recurrence, known.Weekdays, everyTime.Value, everyTitle)
                : new ReminderCommand.Usage(UsageText);
        }
        if (InPattern().Match(rest) is { Success: true } relative)
        {
            var amount = int.Parse(relative.Groups["n"].Value, CultureInfo.InvariantCulture);
            var delay = relative.Groups["u"].Value.ToLowerInvariant() switch
            {
                "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(amount),
                "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
            var title = CleanTitle(relative.Groups["t"].Value);
            if (amount <= 0 || delay > MaxDelay || title is null) return new ReminderCommand.Usage(UsageText);
            return new ReminderCommand.After(delay, title);
        }
        if (AtPattern().Match(rest) is { Success: true } at && ParseTime(at.Groups["h"].Value, at.Groups["m"].Value) is { } time)
        {
            var title = CleanTitle(at.Groups["t"].Value);
            return title is null ? new ReminderCommand.Usage(UsageText) : new ReminderCommand.At(time, false, title);
        }
        if (TomorrowPattern().Match(rest) is { Success: true } tomorrow)
        {
            TimeOnly? morning = tomorrow.Groups["h"].Success
                ? ParseTime(tomorrow.Groups["h"].Value, tomorrow.Groups["m"].Value)
                : DefaultMorning;
            var tomorrowTitle = CleanTitle(tomorrow.Groups["t"].Value);
            return morning is null || tomorrowTitle is null
                ? new ReminderCommand.Usage(UsageText)
                : new ReminderCommand.At(morning.Value, true, tomorrowTitle);
        }
        return new ReminderCommand.Usage(UsageText);
    }

    /// <summary>The moment a command means, in the owner's zone. Never in the past.</summary>
    public static DateTimeOffset Resolve(ReminderCommand command, DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        switch (command)
        {
            case ReminderCommand.After after:
                return utcNow + after.Delay;
            case ReminderCommand.At at:
                var local = TimeZoneInfo.ConvertTime(utcNow, zone);
                var date = DateOnly.FromDateTime(local.DateTime);
                if (at.Tomorrow) date = date.AddDays(1);
                var moment = LocalClock.Resolve(date.ToDateTime(at.Time), zone);
                if (moment <= utcNow && !at.Tomorrow)
                    moment = LocalClock.Resolve(date.AddDays(1).ToDateTime(at.Time), zone);
                return moment;
            case ReminderCommand.Every every:
                var rule = ReminderSchedule.Normalize(every.Recurrence, every.Weekdays, zone.Id, every.Time, null);
                return ReminderSchedule.NextOnOrAfter(utcNow, rule)
                    ?? throw new ArgumentException("That repeat does not fire again.");
            default:
                throw new ArgumentException("Not a reminder with a time.", nameof(command));
        }
    }

    public static string Title(ReminderCommand command) => command switch
    {
        ReminderCommand.After after => after.Title,
        ReminderCommand.At at => at.Title,
        ReminderCommand.Every every => every.Title,
        _ => string.Empty,
    };

    private static int? WeekdayBit(string name)
    {
        name = name.TrimEnd('s');
        return name switch
        {
            "mon" or "monday" => ReminderWeekdays.Monday,
            "tue" or "tues" or "tuesday" => ReminderWeekdays.Tuesday,
            "wed" or "wednesday" => ReminderWeekdays.Wednesday,
            "thu" or "thur" or "thurs" or "thursday" => ReminderWeekdays.Thursday,
            "fri" or "friday" => ReminderWeekdays.Friday,
            "sat" or "saturday" => ReminderWeekdays.Saturday,
            "sun" or "sunday" => ReminderWeekdays.Sunday,
            _ => null,
        };
    }

    /// <summary>The recurrence a command creates, for the reply and the stored reminder.</summary>
    public static CreateReminderRequest ToRequest(ReminderCommand command, DateTimeOffset due, string zoneId) => command switch
    {
        ReminderCommand.Every every => new CreateReminderRequest(every.Title, due, every.Recurrence, every.Weekdays, zoneId,
            null, every.Time),
        _ => new CreateReminderRequest(Title(command), due, null, 0, zoneId),
    };

    private static string? CleanTitle(string raw)
    {
        var clean = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length <= MaxTitleLength ? clean : clean[..MaxTitleLength].TrimEnd();
    }

    private static TimeOnly? ParseTime(string hour, string minute)
    {
        if (!int.TryParse(hour, NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h is < 0 or > 23) return null;
        var m = 0;
        if (minute.Length > 0 && (!int.TryParse(minute, NumberStyles.None, CultureInfo.InvariantCulture, out m) || m is < 0 or > 59))
            return null;
        return new TimeOnly(h, m);
    }

    [GeneratedRegex(@"^(?<n>\d{1,4})\s*(?<u>minutes?|mins?|m|hours?|hrs?|h|days?|d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SnoozePattern();

    [GeneratedRegex(@"^every\s+(?<o>other\s+)?(?<w>[a-z]+)\s+(?:(?:at\s+)?(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?\s+)?(?<t>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex EveryPattern();

    [GeneratedRegex(@"^in\s+(?<n>\d{1,4})\s*(?<u>minutes?|mins?|m|hours?|hrs?|h|days?|d)\b\s*(?<t>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex InPattern();

    [GeneratedRegex(@"^at\s+(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?\s+(?<t>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AtPattern();

    [GeneratedRegex(@"^tomorrow\s+(?:(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?\s+)?(?<t>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TomorrowPattern();
}
