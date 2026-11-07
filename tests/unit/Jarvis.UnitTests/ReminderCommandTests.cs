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
}
