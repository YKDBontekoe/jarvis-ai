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
}
