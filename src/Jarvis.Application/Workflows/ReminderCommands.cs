using System.Globalization;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Workflows;

/// <summary>What a typed reminder command asks for, before it is turned into a moment.</summary>
public abstract record ReminderCommand
{
    /// <summary>In a number of minutes from now.</summary>
    public sealed record After(TimeSpan Delay, string Title) : ReminderCommand;

    /// <summary>At a local clock time, today if it is still ahead, else tomorrow; or on the next day when <paramref name="Tomorrow"/>.</summary>
    public sealed record At(TimeOnly Time, bool Tomorrow, string Title) : ReminderCommand;

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
        "/reminders shows what is coming. Use m, h or d after the number.";

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
        if (command != "/remind") return null;

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
            default:
                throw new ArgumentException("Not a reminder with a time.", nameof(command));
        }
    }

    public static string Title(ReminderCommand command) => command switch
    {
        ReminderCommand.After after => after.Title,
        ReminderCommand.At at => at.Title,
        _ => string.Empty,
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
