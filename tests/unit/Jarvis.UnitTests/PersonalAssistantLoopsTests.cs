using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Jarvis.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PersonalAssistantLoopsTests
{
    [Fact]
    public void GeoDistance_is_zero_for_the_same_point_and_finite_for_known_cities()
    {
        Assert.Equal(0, GeoDistance.Meters(52.0907, 5.1214, 52.0907, 5.1214), 3);
        var amsterdamToUtrecht = GeoDistance.Meters(52.3676, 4.9041, 52.0907, 5.1214);
        Assert.InRange(amsterdamToUtrecht, 30_000, 45_000);
    }

    [Fact]
    public void WatchKinds_normalize_unknown_values_to_public_json()
    {
        Assert.Equal(WatchKinds.PublicJson, WatchKinds.Normalize(null));
        Assert.Equal(WatchKinds.DeviceBattery, WatchKinds.Normalize("device_battery"));
        Assert.Equal(WatchKinds.PublicJson, WatchKinds.Normalize("not-a-kind"));
        Assert.True(WatchKinds.RequiresUrl(WatchKinds.AuthenticatedJson));
        Assert.False(WatchKinds.RequiresUrl(WatchKinds.Calendar));
    }

    [Fact]
    public void Location_watches_trigger_when_distance_is_inside_the_radius()
    {
        var watch = new ConditionWatch(Guid.CreateVersion7(), "Home", "", "", "below", 200, 15,
            WatchKinds.DeviceLocation, latitude: 52.09, longitude: 5.12, radiusMeters: 200);
        Assert.Equal(WatchKinds.DeviceLocation, watch.Kind);
        Assert.True(watch.RecordCheck(80, DateTimeOffset.UtcNow));
        Assert.Equal("triggered", watch.Status);
    }

    [Fact]
    public void Ics_parser_keeps_upcoming_events_and_unfolds_wrapped_summaries()
    {
        var from = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var until = from.AddDays(2);
        var events = IcsCalendarParser.Parse("""
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            DTSTART:20260928T090000Z
            DTEND:20260928T100000Z
            SUMMARY:Standup
            END:VEVENT
            BEGIN:VEVENT
            DTSTART:20260930T090000Z
            SUMMARY:Too far
            END:VEVENT
            BEGIN:VEVENT
            DTSTART:20260928T140000Z
            SUMMARY:Lunch
            END:VEVENT
            END:VCALENDAR
            """, from, until);
        Assert.Equal(2, events.Count);
        Assert.Equal("Standup", events[0].Title);
        Assert.Equal("Lunch", events[1].Title);
    }
}
