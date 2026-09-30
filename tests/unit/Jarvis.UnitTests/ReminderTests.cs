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

    [Fact]
    public void Snooze_brings_a_delivered_reminder_back_with_a_fresh_workflow()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder(Guid.CreateVersion7(), "Call mom", now.AddMinutes(-1));
        reminder.MarkScheduleDispatched();
        reminder.CompleteOccurrence(now, null);
        var firstWorkflow = reminder.WorkflowId;

        reminder.Snooze(now.AddMinutes(10), new TimeOnly(12, 10));

        Assert.Equal("pending", reminder.Status);
        Assert.Null(reminder.CompletedAt);
        Assert.Null(reminder.ScheduleDispatchedAt);
        Assert.Equal(now.AddMinutes(10), reminder.DueAt);
        Assert.NotEqual(firstWorkflow, reminder.WorkflowId);
        Assert.NotNull(reminder.LastDeliveredAt);
    }

    [Fact]
    public void Snooze_and_mark_done_refuse_repeating_or_cancelled_reminders()
    {
        var now = DateTimeOffset.UtcNow;
        var repeating = new Reminder(Guid.CreateVersion7(), "Pills", now.AddHours(1),
            Reminder.RecurrenceDaily, ReminderWeekdays.All, "UTC", new TimeOnly(8, 0), null);
        Assert.Throws<InvalidOperationException>(() => repeating.Snooze(now.AddHours(2), new TimeOnly(9, 0)));
        Assert.Throws<InvalidOperationException>(() => repeating.MarkDone());

        var cancelled = new Reminder(Guid.CreateVersion7(), "Standup", now.AddHours(1));
        cancelled.Cancel();
        Assert.Throws<InvalidOperationException>(() => cancelled.Snooze(now.AddHours(2), new TimeOnly(9, 0)));
    }

    [Fact]
    public void MarkDone_finishes_an_upcoming_reminder_without_a_delivery()
    {
        var reminder = new Reminder(Guid.CreateVersion7(), "Standup", DateTimeOffset.UtcNow.AddHours(1));

        reminder.MarkDone();

        Assert.Equal("completed", reminder.Status);
        Assert.Null(reminder.LastDeliveredAt);
        Assert.Throws<InvalidOperationException>(() => reminder.MarkDone());
    }
}
