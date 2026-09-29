namespace Jarvis.Application.Automations;

public static class AutomationScheduleClock
{
    public static DateTimeOffset? GetNextScheduleFireUtc(DateTimeOffset utcNow, ScheduleTriggerDefinition trigger)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZoneId);
        for (var dayOffset = 0; dayOffset < 370; dayOffset++)
        {
            var localNow = TimeZoneInfo.ConvertTime(utcNow, timeZone);
            var localDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(dayOffset);
            if (trigger.Weekdays is > 0)
            {
                var bit = 1 << ((int)localDate.DayOfWeek + 6) % 7;
                if ((trigger.Weekdays & bit) == 0) continue;
            }

            var candidate = ResolveLocalTime(localDate.ToDateTime(trigger.LocalTime), timeZone);
            if (dayOffset == 0 && candidate <= utcNow) continue;
            return candidate;
        }

        return null;
    }

    public static DateTimeOffset ResolveLocalTime(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var minutes = 0; timeZone.IsInvalidTime(local) && minutes < 180; minutes++)
            local = local.AddMinutes(1);
        if (timeZone.IsInvalidTime(local))
            throw new InvalidOperationException("Could not resolve the local schedule time in the configured time zone.");

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static bool IsWithinTimeWindow(TimeWindowConditionDefinition window, DateTimeOffset utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(window.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (window.StartLocalTime <= window.EndLocalTime)
            return time >= window.StartLocalTime && time <= window.EndLocalTime;
        return time >= window.StartLocalTime || time <= window.EndLocalTime;
    }
}
