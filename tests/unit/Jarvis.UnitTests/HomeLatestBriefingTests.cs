using Jarvis.Application.Workflows;
using Jarvis.Infrastructure.Home;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeLatestBriefingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 14, 0, TimeSpan.Zero);

    private static NotificationRecord Note(string type, TimeSpan age, bool read = false) =>
        new(Guid.CreateVersion7(), type, "Your morning briefing", $"body {age}", null, Now - age,
            read ? Now : null);

    [Fact]
    public void Picks_the_newest_briefing_from_today()
    {
        var today = Note("briefing.daily", TimeSpan.FromHours(1), read: true);
        var earlier = Note("briefing.daily", TimeSpan.FromHours(5));

        var picked = HomeBriefingService.PickLatestBriefing([earlier, Note("reminder.due", TimeSpan.Zero), today], Now);

        Assert.NotNull(picked);
        Assert.Equal(today.Id, picked.Id);
        Assert.True(picked.Read);
    }

    [Fact]
    public void Leaves_out_yesterdays_briefing_and_other_notifications()
    {
        var picked = HomeBriefingService.PickLatestBriefing(
            [Note("briefing.daily", TimeSpan.FromHours(25)), Note("reminder.due", TimeSpan.FromMinutes(5))], Now);

        Assert.Null(picked);
    }
}
