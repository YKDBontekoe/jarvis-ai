using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class BriefingSectionTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000cafe");
    private static readonly DateOnly Today = new(2030, 1, 16);

    [Fact]
    public void Composer_appends_each_section_after_reminders_and_tasks()
    {
        var facts = new DailyBriefingFacts(Today, "UTC", [], [new DailyBriefingItem("Write report", "running")],
        [
            new DailyBriefingSection("Calendar", ["09:30 Standup", "14:00 Dentist"]),
            new DailyBriefingSection("Birthdays", ["Mama — tomorrow"])
        ]);

        var body = DailyBriefingComposer.Compose(facts);

        Assert.Equal("""
            No reminders are due today.

            Active tasks:
            • Write report (running)

            Calendar:
            • 09:30 Standup
            • 14:00 Dentist

            Birthdays:
            • Mama — tomorrow
            """.ReplaceLineEndings("\n"), body);
    }

    [Fact]
    public void Composer_skips_empty_sections_and_stays_unchanged_without_any()
    {
        var plain = DailyBriefingComposer.Compose(new DailyBriefingFacts(Today, "UTC", [], []));
        var withEmpty = DailyBriefingComposer.Compose(new DailyBriefingFacts(Today, "UTC", [], [],
            [new DailyBriefingSection("Calendar", [])]));

        Assert.Equal(plain, withEmpty);
    }

    [Fact]
    public void Long_sections_are_capped_and_the_whole_body_stays_bounded()
    {
        var many = Enumerable.Range(1, 12).Select(i => "Thread " + i).ToArray();

        var section = BriefingSections.Of("Waiting", many)!;

        Assert.Equal(BriefingSections.MaxLines + 1, section.Lines.Count);
        Assert.Equal("…and 7 more", section.Lines[^1]);
        Assert.Null(BriefingSections.Of("Waiting", []));

        var huge = new DailyBriefingFacts(Today, "UTC", [], [],
            Enumerable.Range(0, 40).Select(i => new DailyBriefingSection("S" + i,
                [new string('x', BriefingSections.MaxLineLength)])).ToArray());
        Assert.True(DailyBriefingComposer.Compose(huge).Length <= DailyBriefingComposer.MaxBodyLength);
    }

    [Fact]
    public void Lines_are_flattened_and_shortened()
    {
        Assert.Equal("a b c", BriefingSections.Line("  a\n b\t c  "));
        var line = BriefingSections.Line(new string('y', 500));
        Assert.Equal(BriefingSections.MaxLineLength, line.Length);
        Assert.EndsWith("…", line);
    }

    [Fact]
    public void Commitment_lines_say_who_owes_what_and_how_late()
    {
        Commitment Make(string direction, DateOnly due) => new(Guid.NewGuid(), Owner, direction, "Sam",
            "send the slides", due, "open", false, "manual", null, null, null, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal("You owe Sam: send the slides (today)",
            CommitmentsBriefingSection.Describe(Make("i_owe", Today), Today));
        Assert.Equal("Sam owes you: send the slides (overdue since 14 Jan)",
            CommitmentsBriefingSection.Describe(Make("owed_to_me", Today.AddDays(-2)), Today));
    }

    [Fact]
    public async Task Calendar_section_lists_todays_events_in_local_time_in_order()
    {
        var dayStart = new DateTimeOffset(2030, 1, 15, 23, 0, 0, TimeSpan.Zero); // 00:00 on the 16th in Amsterdam
        var day = new DailyBriefingActivityInput(Owner, "wf", Today, "Europe/Amsterdam", dayStart,
            dayStart.AddDays(1));
        var feed = new Feed(
            new CalendarEventRecord("Dentist", dayStart.AddHours(14), null),
            new CalendarEventRecord("Standup", dayStart.AddHours(9), null),
            new CalendarEventRecord("Yesterday's party", dayStart.AddHours(-3), null));

        var section = await new CalendarBriefingSection(feed).BuildAsync(Owner, day, CancellationToken.None);

        Assert.NotNull(section);
        Assert.Equal("Calendar", section.Title);
        // The events are 9 and 14 hours after local midnight, so they read 09:00 and 14:00 on the owner's clock.
        Assert.Equal(["09:00 Standup", "14:00 Dentist"], section.Lines);
    }

    [Fact]
    public async Task Calendar_section_is_left_out_when_the_feed_is_unreachable()
    {
        var day = new DailyBriefingActivityInput(Owner, "wf", Today, "UTC", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1));

        Assert.Null(await new CalendarBriefingSection(Feed.Failing(new HttpRequestException("down")))
            .BuildAsync(Owner, day, CancellationToken.None));
        Assert.Null(await new CalendarBriefingSection(new Feed()).BuildAsync(Owner, day, CancellationToken.None));
    }

    private sealed class Feed : ICalendarFeed
    {
        private readonly CalendarEventRecord[] _events;
        private readonly Exception? _failure;

        public Feed(params CalendarEventRecord[] events) => _events = events;

        private Feed(Exception failure)
        {
            _events = [];
            _failure = failure;
        }

        public static Feed Failing(Exception failure) => new(failure);

        public Task<IReadOnlyList<CalendarEventRecord>> ListUpcomingAsync(Guid ownerId, DateTimeOffset from,
            DateTimeOffset until, CancellationToken cancellationToken) =>
            _failure is not null ? throw _failure : Task.FromResult<IReadOnlyList<CalendarEventRecord>>(_events);
    }
}
