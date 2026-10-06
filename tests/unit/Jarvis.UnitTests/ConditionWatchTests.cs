using Jarvis.Domain.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConditionWatchTests
{
    [Fact]
    public void Active_watches_are_stale_after_interval_plus_grace()
    {
        var now = DateTimeOffset.UtcNow;
        var watch = new ConditionWatch(Guid.CreateVersion7(), "Price", "https://example.com/metrics.json",
            "value", "above", 100, 15);
        Assert.False(watch.IsScheduleStale(now));
        watch.MarkScheduleDispatched();
        Assert.False(watch.IsScheduleStale(now));
        watch.RecordCheck(1, now.AddMinutes(-(15 + ConditionWatch.ScheduleStaleGraceMinutes + 1)));
        Assert.True(watch.IsScheduleStale(now));
        watch.RecordCheck(1, now);
        Assert.False(watch.IsScheduleStale(now));
        watch.Fail();
        Assert.False(watch.IsScheduleStale(now.AddHours(2)));
    }

    private static ConditionWatch Battery(bool repeat, int cooldown = 60) =>
        new(Guid.CreateVersion7(), "Battery", "", "", "below", 20, 15, WatchKinds.DeviceBattery,
            repeat: repeat, cooldownMinutes: cooldown);

    [Fact]
    public void One_shot_watch_stops_after_the_first_match()
    {
        var watch = Battery(repeat: false);
        var start = DateTimeOffset.UtcNow;

        Assert.False(watch.RecordCheck(50, start));
        Assert.True(watch.RecordCheck(15, start.AddMinutes(15)));
        Assert.Equal("triggered", watch.Status);
        Assert.False(watch.RecordCheck(10, start.AddMinutes(30)));
    }

    [Fact]
    public void Repeating_watch_alerts_once_per_dip_and_stays_active()
    {
        var watch = Battery(repeat: true, cooldown: 30);
        var start = DateTimeOffset.UtcNow;

        Assert.True(watch.RecordCheck(15, start));
        Assert.Equal("active", watch.Status);
        // Still low: no second alert even long after the cooldown, because the condition never cleared.
        Assert.False(watch.RecordCheck(14, start.AddMinutes(45)));
        Assert.False(watch.RecordCheck(13, start.AddHours(5)));
        // Charged, then low again: alerts again.
        Assert.False(watch.RecordCheck(90, start.AddHours(6)));
        Assert.True(watch.RecordCheck(18, start.AddHours(7)));
        Assert.Equal(2, watch.TriggerCount);
        Assert.Null(watch.CompletedAt);
    }

    [Fact]
    public void Repeating_watch_respects_the_cooldown_between_alerts()
    {
        var watch = Battery(repeat: true, cooldown: 60);
        var start = DateTimeOffset.UtcNow;

        Assert.True(watch.RecordCheck(15, start));
        Assert.False(watch.RecordCheck(80, start.AddMinutes(15)));
        // Dipped again after only 30 minutes: held back, but still armed, so it fires once the hour has passed.
        Assert.False(watch.RecordCheck(10, start.AddMinutes(30)));
        Assert.True(watch.RecordCheck(10, start.AddMinutes(61)));
        Assert.Equal(2, watch.TriggerCount);
        Assert.Equal(start.AddMinutes(61), watch.LastTriggeredAt);
    }

    [Fact]
    public void Cooldown_only_applies_to_repeating_watches()
    {
        Assert.Equal(0, Battery(repeat: false, cooldown: 90).CooldownMinutes);
        Assert.Equal(90, Battery(repeat: true, cooldown: 90).CooldownMinutes);
    }
}
