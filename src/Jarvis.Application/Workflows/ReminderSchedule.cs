using System.Globalization;
using System.Text;
using Jarvis.Domain.Workflows;

namespace Jarvis.Application.Workflows;

public readonly record struct ReminderRule(
    string Recurrence,
    int Weekdays,
    string TimeZoneId,
    TimeOnly LocalTime,
    DateOnly? Until);

public static class ReminderWeekdays
{
    public const int Monday = 1;
    public const int Tuesday = 2;
    public const int Wednesday = 4;
    public const int Thursday = 8;
    public const int Friday = 16;
    public const int Saturday = 32;
    public const int Sunday = 64;
    public const int Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday;
    public const int All = Weekdays | Saturday | Sunday;

    public static int Bit(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => 0
    };

    public static int Normalize(string recurrence, int weekdays) => recurrence switch
    {
        Reminder.RecurrenceDaily => All,
        Reminder.RecurrenceWeekdays => Weekdays,
        Reminder.RecurrenceWeekly => weekdays & All,
        _ => 0
    };

    public static IEnumerable<string> Names(int mask)
    {
        if ((mask & Monday) != 0) yield return "Monday";
        if ((mask & Tuesday) != 0) yield return "Tuesday";
        if ((mask & Wednesday) != 0) yield return "Wednesday";
        if ((mask & Thursday) != 0) yield return "Thursday";
        if ((mask & Friday) != 0) yield return "Friday";
        if ((mask & Saturday) != 0) yield return "Saturday";
        if ((mask & Sunday) != 0) yield return "Sunday";
    }
}

public static class ReminderSchedule
{
    public static ReminderRule Normalize(string? recurrence, int weekdays, string? timeZoneId, TimeOnly localTime,
        DateOnly? until)
    {
        var kind = (recurrence ?? Reminder.RecurrenceNone).Trim().ToLowerInvariant();
        if (kind is "weekday") kind = Reminder.RecurrenceWeekdays;
        if (!Reminder.RecurrenceKinds.Contains(kind))
            throw new ArgumentException("Recurrence must be none, daily, weekdays, or weekly.");
        if (!LocalClock.TryFind(timeZoneId, out var zone))
            throw new ArgumentException("Time zone identifier is not recognized by this server.");
        var mask = ReminderWeekdays.Normalize(kind, weekdays);
        if (kind == Reminder.RecurrenceWeekly && mask == 0)
            throw new ArgumentException("Choose at least one weekday for a weekly reminder.");
        return new ReminderRule(kind, mask, zone.Id, localTime, until);
    }

    public static ReminderRule FromRecord(ReminderRecord reminder)
    {
        var localTime = reminder.LocalTime ?? TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(reminder.DueAt,
                LocalClock.TryFind(reminder.TimeZoneId, out var zone) ? zone : TimeZoneInfo.Utc).DateTime);
        return Normalize(reminder.Recurrence, reminder.Weekdays, reminder.TimeZoneId, localTime, reminder.Until);
    }

    public static DateTimeOffset ResolveFirst(DateTimeOffset requestedDueAt, ReminderRule rule, DateTimeOffset utcNow)
    {
        var requested = requestedDueAt.ToUniversalTime();
        if (rule.Recurrence == Reminder.RecurrenceNone)
        {
            if (requested <= utcNow)
                throw new ArgumentOutOfRangeException(nameof(requestedDueAt), "Reminder time must be in the future.");
            return requested;
        }

        if (requested > utcNow && Matches(requested, rule) && !IsAfterUntil(requested, rule))
            return requested;

        return NextOnOrAfter(utcNow.AddSeconds(1), rule)
            ?? throw new ArgumentOutOfRangeException(nameof(requestedDueAt),
                "That recurrence does not fire again before its end date.");
    }

    public static DateTimeOffset? NextAfter(DateTimeOffset previousDueAt, ReminderRule rule) =>
        NextOnOrAfter(previousDueAt.ToUniversalTime().AddSeconds(1), rule);

    public static DateTimeOffset? NextOnOrAfter(DateTimeOffset utcNow, ReminderRule rule)
    {
        if (rule.Recurrence == Reminder.RecurrenceNone) return null;
        if (!LocalClock.TryFind(rule.TimeZoneId, out var zone))
            throw new ArgumentException("Time zone identifier is not recognized by this server.");

        var mask = ReminderWeekdays.Normalize(rule.Recurrence, rule.Weekdays);
        if (mask == 0) mask = ReminderWeekdays.All;
        var now = utcNow.ToUniversalTime();
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(localNow.DateTime);
        for (var offset = 0; offset <= 8; offset++)
        {
            var candidateDate = date.AddDays(offset);
            if (rule.Until is { } until && candidateDate > until) return null;
            if ((ReminderWeekdays.Bit(candidateDate.DayOfWeek) & mask) == 0) continue;
            var fireAt = LocalClock.Resolve(candidateDate.ToDateTime(rule.LocalTime), zone);
            if (fireAt >= now) return fireAt;
        }

        return null;
    }

    public static bool Matches(DateTimeOffset instant, ReminderRule rule)
    {
        if (rule.Recurrence == Reminder.RecurrenceNone) return true;
        if (!LocalClock.TryFind(rule.TimeZoneId, out var zone)) return false;
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var time = TimeOnly.FromTimeSpan(local.TimeOfDay);
        if (Math.Abs((time - rule.LocalTime).TotalMinutes) > 1) return false;
        var mask = ReminderWeekdays.Normalize(rule.Recurrence, rule.Weekdays);
        return mask == 0 || (ReminderWeekdays.Bit(local.DayOfWeek) & mask) != 0;
    }

    public static string Describe(ReminderRule rule)
    {
        var time = rule.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        return rule.Recurrence switch
        {
            Reminder.RecurrenceDaily => $"every day at {time}",
            Reminder.RecurrenceWeekdays => $"every weekday at {time}",
            Reminder.RecurrenceWeekly => DescribeWeekly(rule.Weekdays, time),
            _ => $"at {time}"
        };
    }

    public static string Describe(ReminderRecord reminder) =>
        reminder.Recurrence == Reminder.RecurrenceNone
            ? string.Empty
            : Describe(FromRecord(reminder));

    private static bool IsAfterUntil(DateTimeOffset instant, ReminderRule rule)
    {
        if (rule.Until is null) return false;
        if (!LocalClock.TryFind(rule.TimeZoneId, out var zone)) return false;
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).Date);
        return localDate > rule.Until;
    }

    private static string DescribeWeekly(int weekdays, string time)
    {
        var names = ReminderWeekdays.Names(weekdays).ToArray();
        if (names.Length == 0) return $"weekly at {time}";
        if (names.Length == 1) return $"every {names[0]} at {time}";
        var builder = new StringBuilder("every ");
        builder.Append(string.Join(", ", names.Take(names.Length - 1)));
        builder.Append(" and ").Append(names[^1]).Append(" at ").Append(time);
        return builder.ToString();
    }
}
