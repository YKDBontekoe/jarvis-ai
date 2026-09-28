using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ReminderScheduleTests
{
    [Fact]
    public void Weekdays_skip_the_weekend_and_keep_the_local_time()
    {
        var rule = ReminderSchedule.Normalize("weekdays", 0, "Europe/Amsterdam", new TimeOnly(7, 30), null);
        var friday = new DateTimeOffset(2030, 1, 18, 8, 0, 0, TimeSpan.FromHours(1));

        var next = ReminderSchedule.NextAfter(friday, rule);

        Assert.Equal(new DateTimeOffset(2030, 1, 21, 7, 30, 0, TimeSpan.FromHours(1)), next);
    }

    [Fact]
    public void Until_stops_the_series()
    {
        var rule = ReminderSchedule.Normalize("daily", 0, "UTC", new TimeOnly(9, 0), new DateOnly(2030, 1, 16));
        var last = new DateTimeOffset(2030, 1, 16, 9, 0, 0, TimeSpan.Zero);

        Assert.Null(ReminderSchedule.NextAfter(last, rule));
    }

    [Fact]
    public void Invalid_dst_times_skip_forward()
    {
        var rule = ReminderSchedule.Normalize("daily", 0, "Europe/Amsterdam", new TimeOnly(2, 30), null);
        var beforeGap = new DateTimeOffset(2026, 3, 28, 2, 30, 0, TimeSpan.FromHours(1));

        var next = ReminderSchedule.NextAfter(beforeGap, rule);

        Assert.NotNull(next);
        var local = TimeZoneInfo.ConvertTime(next!.Value, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));
        Assert.Equal(new DateOnly(2026, 3, 29), DateOnly.FromDateTime(local.DateTime));
        Assert.Equal(3, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    [Fact]
    public void One_shot_reminders_still_complete()
    {
        var reminder = new Reminder(Guid.CreateVersion7(), "Standup", DateTimeOffset.UtcNow.AddHours(1));
        reminder.CompleteOccurrence(DateTimeOffset.UtcNow, null);
        Assert.Equal("completed", reminder.Status);
        Assert.NotNull(reminder.LastDeliveredAt);
    }

    [Fact]
    public void Recurring_occurrence_moves_the_next_due_time()
    {
        var first = DateTimeOffset.UtcNow.AddHours(1);
        var reminder = new Reminder(Guid.CreateVersion7(), "Trash", first, Reminder.RecurrenceWeekdays,
            ReminderWeekdays.Weekdays, "UTC", new TimeOnly(7, 30), null);
        var next = first.AddDays(1);
        reminder.CompleteOccurrence(DateTimeOffset.UtcNow, next);
        Assert.Equal("pending", reminder.Status);
        Assert.Equal(next.ToUniversalTime(), reminder.DueAt);
        Assert.NotNull(reminder.LastDeliveredAt);
    }

    [Fact]
    public void Weekly_requires_at_least_one_day()
    {
        Assert.Throws<ArgumentException>(() =>
            ReminderSchedule.Normalize("weekly", 0, "UTC", new TimeOnly(8, 0), null));
    }

    [Fact]
    public void Recurring_first_fire_uses_the_rule_local_time_in_the_zone()
    {
        var rule = ReminderSchedule.Normalize("weekdays", 0, "Europe/Amsterdam", new TimeOnly(7, 30), null);
        var requested = new DateTimeOffset(2030, 1, 16, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2030, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var first = ReminderSchedule.ResolveFirst(requested, rule, now);
        var local = TimeZoneInfo.ConvertTime(first, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));

        Assert.Equal(new TimeOnly(7, 30), TimeOnly.FromTimeSpan(local.TimeOfDay));
        Assert.NotEqual(DayOfWeek.Saturday, local.DayOfWeek);
        Assert.NotEqual(DayOfWeek.Sunday, local.DayOfWeek);
    }
}

public sealed class DailyBriefingComposerTests
{
    [Fact]
    public void Recurring_reminders_appear_in_todays_facts()
    {
        var due = new DateTimeOffset(2030, 1, 16, 7, 30, 0, TimeSpan.FromHours(1));
        var reminder = new ReminderRecord(Guid.NewGuid(), Guid.NewGuid(), "Take out the trash", due, "wf", "pending",
            due, null, Reminder.RecurrenceWeekdays, ReminderWeekdays.Weekdays, "Europe/Amsterdam", new TimeOnly(7, 30));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var facts = new DailyBriefingFacts(new DateOnly(2030, 1, 16), "Europe/Amsterdam",
            [DailyBriefingComposer.ReminderItem(reminder, zone)], []);

        var body = DailyBriefingComposer.Compose(facts);

        Assert.Contains("Take out the trash", body);
        Assert.Contains("07:30", body);
        Assert.Contains("weekday", body);
    }

    [Fact]
    public void Failed_narration_keeps_the_fact_list()
    {
        var facts = DailyBriefingComposer.Compose(new DailyBriefingFacts(new DateOnly(2030, 1, 16), "UTC", [], []));
        Assert.Equal(facts, DailyBriefingComposer.Combine(null, facts));
        Assert.StartsWith("Quiet morning.", DailyBriefingComposer.Combine("Quiet morning.", facts));
        Assert.Contains("No reminders are due today.", DailyBriefingComposer.Combine("Quiet morning.", facts));
    }
}
