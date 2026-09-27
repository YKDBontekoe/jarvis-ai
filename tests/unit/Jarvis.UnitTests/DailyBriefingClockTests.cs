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
}
