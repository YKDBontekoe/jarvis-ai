using Jarvis.Application.Workflows;
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

    [Fact]
    public void Overdue_dispatched_recurring_reminders_stay_pending_for_the_next_fire()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder(Guid.CreateVersion7(), "Trash", now.AddMinutes(-5),
            Reminder.RecurrenceWeekdays, ReminderWeekdays.Weekdays, "UTC", new TimeOnly(7, 30), null);
        reminder.MarkScheduleDispatched();
        Assert.True(reminder.IsOverdueDispatchStale(now));
        Assert.Equal("pending", reminder.Status);

        reminder.CompleteOccurrence(now, now.AddDays(1));
        Assert.Equal("pending", reminder.Status);
        Assert.False(reminder.IsOverdueDispatchStale(now));
        Assert.NotNull(reminder.LastDeliveredAt);
    }

    [Fact]
    public void AttachConversation_stores_the_linked_chat()
    {
        var reminder = new Reminder(Guid.CreateVersion7(), "Standup", DateTimeOffset.UtcNow.AddHours(1));
        var conversationId = Guid.CreateVersion7();
        reminder.AttachConversation(conversationId);
        Assert.Equal(conversationId, reminder.ConversationId);
        Assert.Throws<ArgumentException>(() => reminder.AttachConversation(Guid.Empty));
    }
}
