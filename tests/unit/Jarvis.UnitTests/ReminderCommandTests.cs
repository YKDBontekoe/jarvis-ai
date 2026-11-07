using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ReminderCommandTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // 2026-11-07 21:30 in Amsterdam (UTC+1).
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 20, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("/remind in 30m call mum", 30, "call mum")]
    [InlineData("/REMIND in 2 hours stretch", 120, "stretch")]
    [InlineData("/remind in 1d renew passport", 1440, "renew passport")]
    [InlineData("/remind in 5 min tea", 5, "tea")]
    public void Relative_times_are_read(string text, int minutes, string title)
    {
        var command = Assert.IsType<ReminderCommand.After>(ReminderCommands.Parse(text));

        Assert.Equal(TimeSpan.FromMinutes(minutes), command.Delay);
        Assert.Equal(title, command.Title);
        Assert.Equal(Now.AddMinutes(minutes), ReminderCommands.Resolve(command, Now, Amsterdam));
    }

    [Fact]
    public void A_clock_time_is_today_when_still_ahead_and_tomorrow_when_it_has_passed()
    {
        var evening = Assert.IsType<ReminderCommand.At>(ReminderCommands.Parse("/remind at 23:15 lock up"));
        var morning = Assert.IsType<ReminderCommand.At>(ReminderCommands.Parse("/remind at 8.30 gym"));

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 23, 15, 0, TimeSpan.FromHours(1)), ReminderCommands.Resolve(evening, Now, Amsterdam));
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 8, 30, 0, TimeSpan.FromHours(1)), ReminderCommands.Resolve(morning, Now, Amsterdam));
    }

    [Fact]
    public void Tomorrow_uses_the_given_time_or_nine_in_the_morning()
    {
        var timed = Assert.IsType<ReminderCommand.At>(ReminderCommands.Parse("/remind tomorrow 14:00 dentist"));
        var plain = Assert.IsType<ReminderCommand.At>(ReminderCommands.Parse("/remind tomorrow call the bank"));

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.FromHours(1)), ReminderCommands.Resolve(timed, Now, Amsterdam));
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(1)), ReminderCommands.Resolve(plain, Now, Amsterdam));
        Assert.Equal("call the bank", plain.Title);
    }

    [Theory]
    [InlineData("/remind")]
    [InlineData("/remind in 30m")]
    [InlineData("/remind in 0m nothing")]
    [InlineData("/remind in 9999d far away")]
    [InlineData("/remind at 25:00 never")]
    [InlineData("/remind at 18:61 never")]
    [InlineData("/remind yesterday something")]
    [InlineData("/remind soon call mum")]
    public void Unreadable_commands_explain_the_format(string text) =>
        Assert.Equal(ReminderCommands.UsageText, Assert.IsType<ReminderCommand.Usage>(ReminderCommands.Parse(text)).Message);

    [Theory]
    [InlineData("remind me in 30m")]
    [InlineData("/lists")]
    [InlineData("/remindme")]
    [InlineData("")]
    public void Other_messages_are_not_reminder_commands(string text) => Assert.Null(ReminderCommands.Parse(text));

    [Fact]
    public void Reminders_lists_and_titles_are_flattened_and_capped()
    {
        Assert.IsType<ReminderCommand.List>(ReminderCommands.Parse("/reminders"));
        var capped = Assert.IsType<ReminderCommand.After>(ReminderCommands.Parse("/remind in 5m " + new string('x', 500) + "\u0007"));
        Assert.Equal(ReminderCommands.MaxTitleLength, capped.Title.Length);
    }

    [Theory]
    [InlineData("/remind every monday 9:00 put the bins out", "weekly", 1, 9, 0, "put the bins out")]
    [InlineData("/remind every Fri at 17.30 send the report", "weekly", 16, 17, 30, "send the report")]
    [InlineData("/remind every other friday 17:00 report", "biweekly", 16, 17, 0, "report")]
    [InlineData("/remind every day 8:00 take pills", "daily", 0, 8, 0, "take pills")]
    [InlineData("/remind every weekday stand-up notes", "weekdays", 0, 9, 0, "stand-up notes")]
    [InlineData("/remind every sundays call mum", "weekly", 64, 9, 0, "call mum")]
    public void Repeating_reminders_are_read(string text, string recurrence, int weekdays, int hour, int minute, string title)
    {
        var every = Assert.IsType<ReminderCommand.Every>(ReminderCommands.Parse(text));

        Assert.Equal(recurrence, every.Recurrence);
        Assert.Equal(weekdays, every.Weekdays);
        Assert.Equal(new TimeOnly(hour, minute), every.Time);
        Assert.Equal(title, every.Title);
    }

    [Theory]
    [InlineData("/remind every blursday 9:00 nothing")]
    [InlineData("/remind every other day 9:00 nothing")]
    [InlineData("/remind every monday")]
    [InlineData("/remind every monday 25:00 nothing")]
    public void Unreadable_repeats_explain_the_format(string text) =>
        Assert.Equal(ReminderCommands.UsageText, Assert.IsType<ReminderCommand.Usage>(ReminderCommands.Parse(text)).Message);

    [Fact]
    public void The_first_repeat_is_the_next_matching_moment_in_the_owners_zone_and_the_request_carries_the_rule()
    {
        // Saturday 7 November 2026, 21:30 in Amsterdam.
        var monday = Assert.IsType<ReminderCommand.Every>(ReminderCommands.Parse("/remind every monday 9:00 bins"));
        var later = Assert.IsType<ReminderCommand.Every>(ReminderCommands.Parse("/remind every day 22:00 pills"));

        var first = ReminderCommands.Resolve(monday, Now, Amsterdam);
        var tonight = ReminderCommands.Resolve(later, Now, Amsterdam);
        var request = ReminderCommands.ToRequest(monday, first, Amsterdam.Id);

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(1)), first);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 22, 0, 0, TimeSpan.FromHours(1)), tonight);
        Assert.Equal("weekly", request.Recurrence);
        Assert.Equal(1, request.Weekdays);
        Assert.Equal(new TimeOnly(9, 0), request.LocalTime);
        Assert.Equal(Amsterdam.Id, request.TimeZoneId);
    }
}
