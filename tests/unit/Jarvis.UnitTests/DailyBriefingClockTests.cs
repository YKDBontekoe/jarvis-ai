using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DailyBriefingClockTests
{
    [Fact]
    public void Resolves_the_same_day_when_the_local_time_is_still_ahead()
    {
        var now = new DateTimeOffset(2026, 3, 10, 6, 0, 0, TimeSpan.Zero);
        var schedule = DailyBriefingClock.ResolveNext(now, new TimeOnly(8, 0), "UTC");

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero), schedule.FireAt);
        Assert.Equal(new DateOnly(2026, 3, 10), schedule.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), schedule.LocalDayStart);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), schedule.NextLocalDayStart);
    }

    [Fact]
    public void Rolls_to_the_next_day_after_the_local_time_has_passed()
    {
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var schedule = DailyBriefingClock.ResolveNext(now, new TimeOnly(8, 0), "UTC");

        Assert.Equal(new DateTimeOffset(2026, 3, 11, 8, 0, 0, TimeSpan.Zero), schedule.FireAt);
        Assert.Equal(new DateOnly(2026, 3, 11), schedule.LocalDate);
    }

    [Fact]
    public void Catches_up_immediately_when_today_was_not_delivered()
    {
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var schedule = DailyBriefingClock.ResolveNext(now, new TimeOnly(8, 0), "UTC",
            lastDeliveredDate: new DateOnly(2026, 3, 9));

        Assert.Equal(now, schedule.FireAt);
        Assert.Equal(new DateOnly(2026, 3, 10), schedule.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), schedule.LocalDayStart);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), schedule.NextLocalDayStart);
    }

    [Fact]
    public void Catches_up_immediately_when_nothing_has_been_delivered_yet()
    {
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var schedule = DailyBriefingClock.ResolveNext(now, new TimeOnly(8, 0), "UTC", lastDeliveredDate: null);

        Assert.Equal(now, schedule.FireAt);
        Assert.Equal(new DateOnly(2026, 3, 10), schedule.LocalDate);
    }

    [Fact]
    public void Still_waits_for_today_when_catch_up_has_not_reached_the_local_time()
    {
        var now = new DateTimeOffset(2026, 3, 10, 6, 0, 0, TimeSpan.Zero);
        var schedule = DailyBriefingClock.ResolveNext(now, new TimeOnly(8, 0), "UTC", lastDeliveredDate: null);

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero), schedule.FireAt);
        Assert.Equal(new DateOnly(2026, 3, 10), schedule.LocalDate);
    }

    [Fact]
    public void Rolls_to_tomorrow_once_today_was_delivered()
    {
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var schedule = DailyBriefingClock.ResolveNext(now, new TimeOnly(8, 0), "UTC",
            lastDeliveredDate: new DateOnly(2026, 3, 10));

        Assert.Equal(new DateTimeOffset(2026, 3, 11, 8, 0, 0, TimeSpan.Zero), schedule.FireAt);
        Assert.Equal(new DateOnly(2026, 3, 11), schedule.LocalDate);
    }
}
