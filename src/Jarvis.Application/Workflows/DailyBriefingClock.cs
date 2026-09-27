namespace Jarvis.Application.Workflows;

/// <summary>Resolves the next local briefing instant. Used from Temporal activities, not workflows.</summary>
public static class DailyBriefingClock
{
    public static DailyBriefingSchedule ResolveNext(DateTimeOffset utcNow, TimeOnly localTime, string timeZoneId) =>
        ResolveNext(utcNow, localTime, timeZoneId, lastDeliveredDate: null, catchUpMissedDay: false);

    public static DailyBriefingSchedule ResolveNext(DateTimeOffset utcNow, TimeOnly localTime, string timeZoneId,
        DateOnly? lastDeliveredDate) =>
        ResolveNext(utcNow, localTime, timeZoneId, lastDeliveredDate, catchUpMissedDay: true);

    private static DailyBriefingSchedule ResolveNext(DateTimeOffset utcNow, TimeOnly localTime, string timeZoneId,
        DateOnly? lastDeliveredDate, bool catchUpMissedDay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var now = utcNow.ToUniversalTime();
        var fireAt = GetNextOccurrence(now, localTime, timeZone, lastDeliveredDate, catchUpMissedDay);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fireAt.UtcDateTime, timeZone).Date);
        var dayStart = ResolveLocalTime(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
        var nextDayStart = ResolveLocalTime(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);
        return new DailyBriefingSchedule(fireAt, localDate, dayStart, nextDayStart);
    }

    public static bool IsDispatchStale(bool enabled, DateTimeOffset? scheduleDispatchedAt, TimeOnly localTime,
        string timeZoneId, DateOnly? lastDeliveredDate, DateTimeOffset utcNow)
    {
        if (!enabled || scheduleDispatchedAt is null) return false;
        if (scheduleDispatchedAt > utcNow.AddMinutes(-30)) return false;
        try
        {
            return ResolveNext(utcNow, localTime, timeZoneId, lastDeliveredDate).FireAt <= utcNow;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static DateTimeOffset GetNextOccurrence(DateTimeOffset now, TimeOnly localTime, TimeZoneInfo timeZone,
        DateOnly? lastDeliveredDate, bool catchUpMissedDay)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        if (catchUpMissedDay && lastDeliveredDate >= localDate)
            return ResolveLocalTime(localDate.AddDays(1).ToDateTime(localTime), timeZone);

        var candidate = ResolveLocalTime(localDate.ToDateTime(localTime), timeZone);
        if (candidate > now) return candidate;
        if (catchUpMissedDay) return now;
        return ResolveLocalTime(localDate.AddDays(1).ToDateTime(localTime), timeZone);
    }

    private static DateTimeOffset ResolveLocalTime(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var minutes = 0; timeZone.IsInvalidTime(local) && minutes < 180; minutes++)
            local = local.AddMinutes(1);
        if (timeZone.IsInvalidTime(local))
            throw new InvalidOperationException("Could not resolve the local briefing time in the configured time zone.");

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
