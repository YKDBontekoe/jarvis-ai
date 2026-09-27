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

    [Fact]
    public void Overdue_dispatched_reminders_are_stale_for_reschedule()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder(Guid.CreateVersion7(), "Standup", now.AddMinutes(-5));
        Assert.False(reminder.IsOverdueDispatchStale(now));
        reminder.MarkScheduleDispatched();
        Assert.True(reminder.IsOverdueDispatchStale(now));
        reminder.Complete();
        Assert.False(reminder.IsOverdueDispatchStale(now));
    }
}
