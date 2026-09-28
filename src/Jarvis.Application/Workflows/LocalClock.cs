namespace Jarvis.Application.Workflows;

/// <summary>Resolves a local civil time in an IANA zone, skipping invalid DST gaps.</summary>
public static class LocalClock
{
    public static bool TryFind(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        var id = timeZoneId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Z", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.Length > 100) return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static DateTimeOffset Resolve(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var hours = 0; timeZone.IsInvalidTime(local) && hours < 3; hours++)
            local = local.AddHours(1);
        for (var minutes = 0; timeZone.IsInvalidTime(local) && minutes < 180; minutes++)
            local = local.AddMinutes(1);
        if (timeZone.IsInvalidTime(local))
            throw new InvalidOperationException("Could not resolve the local time in the configured time zone.");

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
