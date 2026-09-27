using Jarvis.Domain.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ReminderTests
{
    [Fact]
    public void FailScheduling_only_applies_to_pending_reminders()
    {
        var reminder = new Reminder(Guid.CreateVersion7(), "Standup", DateTimeOffset.UtcNow.AddHours(1));
        reminder.FailScheduling();
        Assert.Equal("failed", reminder.Status);
        reminder.Complete();
        Assert.Equal("failed", reminder.Status);
    }
}
