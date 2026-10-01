using Jarvis.Agents;
using Jarvis.Application.Persona;
using Jarvis.Application.Reviews;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WeeklyReviewClockTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly TimeOnly Evening = new(19, 0);

    [Theory]
    [InlineData("2026-09-28", "2026-09-28")]
    [InlineData("2026-10-01", "2026-09-28")]
    [InlineData("2026-10-04", "2026-09-28")]
    [InlineData("2026-10-05", "2026-10-05")]
    public void Weeks_start_on_monday(string date, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), WeeklyReviewClock.WeekStartOf(DateOnly.Parse(date)));

    [Fact]
    public void Midweek_waits_for_sunday_evening_in_the_owner_zone()
    {
        var (fireAt, week) = WeeklyReviewClock.ResolveNext(DateTimeOffset.Parse("2026-10-01T12:00:00Z"), Evening,
            Amsterdam, lastNotifiedWeek: new DateOnly(2026, 9, 21));

        Assert.Equal(new DateOnly(2026, 9, 28), week);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T17:00:00Z"), fireAt);
    }

    [Fact]
    public void Delivered_week_moves_on_to_next_sunday()
    {
        var (fireAt, week) = WeeklyReviewClock.ResolveNext(DateTimeOffset.Parse("2026-10-04T17:05:00Z"), Evening,
            Amsterdam, lastNotifiedWeek: new DateOnly(2026, 9, 28));

        Assert.Equal(new DateOnly(2026, 10, 5), week);
        Assert.Equal(DateTimeOffset.Parse("2026-10-11T17:00:00Z"), fireAt);
    }

    [Fact]
    public void Sunday_missed_by_a_few_hours_is_sent_right_away()
    {
        var now = DateTimeOffset.Parse("2026-10-05T06:00:00Z");

        var (fireAt, week) = WeeklyReviewClock.ResolveNext(now, Evening, Amsterdam,
            lastNotifiedWeek: new DateOnly(2026, 9, 21));

        Assert.Equal(new DateOnly(2026, 9, 28), week);
        Assert.Equal(now, fireAt);
    }

    [Fact]
    public void Sunday_missed_for_days_is_skipped()
    {
        var (fireAt, week) = WeeklyReviewClock.ResolveNext(DateTimeOffset.Parse("2026-10-07T12:00:00Z"), Evening,
            Amsterdam, lastNotifiedWeek: null);

        Assert.Equal(new DateOnly(2026, 10, 5), week);
        Assert.Equal(DateTimeOffset.Parse("2026-10-11T17:00:00Z"), fireAt);
    }

    [Fact]
    public void Daylight_saving_change_keeps_local_evening()
    {
        var (fireAt, week) = WeeklyReviewClock.ResolveNext(DateTimeOffset.Parse("2026-10-21T12:00:00Z"), Evening,
            Amsterdam, lastNotifiedWeek: new DateOnly(2026, 10, 12));

        Assert.Equal(new DateOnly(2026, 10, 19), week);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T18:00:00Z"), fireAt);
    }

    [Fact]
    public void Settings_reject_unknown_zone_and_trim_seconds()
    {
        Assert.Throws<ArgumentException>(() =>
            new WeeklyReviewSettings(true, Evening, "Mars/Olympus").Normalize());
        var normalized = new WeeklyReviewSettings(true, new TimeOnly(19, 30, 42), " Europe/Amsterdam ").Normalize();
        Assert.Equal(new TimeOnly(19, 30), normalized.LocalTime);
        Assert.Equal("Europe/Amsterdam", normalized.TimeZoneId);
    }
}

public sealed class WeeklyReviewComposerTests
{
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public void Stats_average_ratings_and_find_the_best_day()
    {
        var stats = WeeklyReviewComposer.BuildStats(
        [
            new JournalSample(Monday, 3, 2, 4, 5, ["work"]),
            new JournalSample(Monday.AddDays(2), 4, 4, 2, 8, ["Work", "run"]),
            new JournalSample(Monday.AddDays(5), 5, null, 1, 7, ["run", "family"])
        ], previousMood: 3.5, tasksCompleted: 2, remindersHandled: 4, remindersUpcoming: 1, newMemories: 3);

        Assert.Equal(3, stats.JournalEntries);
        Assert.Equal(4.0, stats.Mood);
        Assert.Equal(3.0, stats.Energy);
        Assert.Equal(Monday.AddDays(2), stats.BestDay);
        Assert.Equal(8, stats.BestDayRating);
        Assert.Equal(["run", "work", "family"], stats.TopTags);
        Assert.Equal(3, stats.Days.Count);
    }

    [Fact]
    public void Story_reads_the_numbers_back_plainly()
    {
        var stats = WeeklyReviewComposer.BuildStats(
            [new JournalSample(Monday, 4, 3, 2, 8, []), new JournalSample(Monday.AddDays(3), 4, 3, 2, 6, [])],
            previousMood: 3.5, tasksCompleted: 1, remindersHandled: 2, remindersUpcoming: 3, newMemories: 0);

        var story = WeeklyReviewComposer.Compose(Facts(stats));

        Assert.Contains("You journaled on 2 days this week", story);
        Assert.Contains("mood averaged 4.0/5, up 0.5 on last week", story);
        Assert.Contains("Your best day was Monday (8/10).", story);
        Assert.Contains("1 task finished and 2 reminders handled.", story);
        Assert.Contains("Next week has 3 reminders lined up.", story);
    }

    [Fact]
    public void Quiet_week_says_so_without_judging()
    {
        var stats = WeeklyReviewComposer.BuildStats([], null, 0, 0, 0, 0);

        Assert.StartsWith("A quiet week", WeeklyReviewComposer.Compose(Facts(stats)));
    }

    [Fact]
    public void Trend_keeps_empty_weeks_in_order()
    {
        var trend = WeeklyReviewComposer.Trend(
        [
            new JournalSample(Monday.AddDays(-13), 2, null, null, null, []),
            new JournalSample(Monday.AddDays(1), 4, null, null, 7, []),
            new JournalSample(Monday.AddDays(4), 5, null, null, 9, [])
        ], Monday, weeks: 3);

        Assert.Equal([Monday.AddDays(-14), Monday.AddDays(-7), Monday], trend.Select(point => point.WeekStart));
        Assert.Equal(2.0, trend[0].Mood);
        Assert.Null(trend[1].Mood);
        Assert.Equal(0, trend[1].Entries);
        Assert.Equal(4.5, trend[2].Mood);
        Assert.Equal(8.0, trend[2].Rating);
    }

    [Fact]
    public void Narration_drops_code_fences_and_notification_is_bounded()
    {
        Assert.Equal("A good week.", WeeklyReviewComposer.CleanNarration("```text\nA good week.\n```"));
        var body = WeeklyReviewComposer.NotificationBody(new string('a', 900));
        Assert.Equal(WeeklyReviewComposer.MaxNotificationLength, body.Length);
    }

    internal static WeeklyReviewFacts Facts(WeeklyReviewStats stats) =>
        new(Monday, "Europe/Amsterdam", stats, ["Long walk"], ["Book dentist"], ["Likes tea"]);
}

public sealed class WeeklyReviewServiceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000feed");
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public async Task Turned_off_review_stops_the_workflow()
    {
        var service = Service(new InMemorySettingsStore(), Repository(), new FixedNarrator(null));

        Assert.False((await service.ResolveScheduleAsync(Owner, CancellationToken.None)).Continue);
        Assert.False(await service.DeliverAsync(new WeeklyReviewActivityInput(Owner, Monday), CancellationToken.None));
    }

    [Fact]
    public async Task Delivery_falls_back_to_plain_story_and_notifies()
    {
        var settings = await EnabledSettings();
        (string Story, bool Narrated, bool Notify)? saved = null;
        var repository = Repository(onSave: (story, narrated, notify) => saved = (story, narrated, notify));

        var keepGoing = await Service(settings, repository, new FixedNarrator(null))
            .DeliverAsync(new WeeklyReviewActivityInput(Owner, Monday), CancellationToken.None);

        Assert.True(keepGoing);
        Assert.NotNull(saved);
        Assert.False(saved.Value.Narrated);
        Assert.True(saved.Value.Notify);
        Assert.StartsWith("A quiet week", saved.Value.Story);
    }

    [Fact]
    public async Task Delivery_uses_the_narrator_story_when_it_answers()
    {
        var settings = await EnabledSettings();
        string? story = null;
        var repository = Repository(onSave: (text, _, _) => story = text);

        await Service(settings, repository, new FixedNarrator("Lovely week, Youri."))
            .DeliverAsync(new WeeklyReviewActivityInput(Owner, Monday), CancellationToken.None);

        Assert.Equal("Lovely week, Youri.", story);
    }

    [Fact]
    public async Task Already_delivered_week_is_not_sent_twice()
    {
        var settings = await EnabledSettings();
        var saves = 0;
        var repository = Repository(notified: true, onSave: (_, _, _) => saves++);

        Assert.True(await Service(settings, repository, new FixedNarrator(null))
            .DeliverAsync(new WeeklyReviewActivityInput(Owner, Monday), CancellationToken.None));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task Generate_now_writes_current_week_without_notifying()
    {
        var settings = await EnabledSettings();
        (DateOnly Week, bool Notify)? saved = null;
        var repository = Repository(onSaveFacts: (facts, notify) => saved = (facts.WeekStart, notify));

        await Service(settings, repository, new FixedNarrator(null))
            .GenerateNowAsync(Owner, CancellationToken.None);

        Assert.Equal((Monday, false), saved);
    }

    private static async Task<InMemorySettingsStore> EnabledSettings()
    {
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.WeeklyReview,
            new WeeklyReviewSettings(true, new TimeOnly(19, 0), "Europe/Amsterdam"), CancellationToken.None);
        return settings;
    }

    private static WeeklyReviewService Service(IOwnerSettingsStore settings, IWeeklyReviewRepository repository,
        IWeeklyReviewNarrator narrator) =>
        new(settings, repository, narrator,
            Fake<IWeeklyReviewScheduler>.Create(("ScheduleWeeklyReviewAsync", _ => Task.CompletedTask)),
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => (DailyBriefingPreferenceRecord?)null)),
            new FixedTime(DateTimeOffset.Parse("2026-10-01T12:00:00Z")),
            NullLogger<WeeklyReviewService>.Instance);

    private static IWeeklyReviewRepository Repository(bool notified = false,
        Action<string, bool, bool>? onSave = null, Action<WeeklyReviewFacts, bool>? onSaveFacts = null) =>
        Fake<IWeeklyReviewRepository>.Create(
            ("IsNotifiedAsync", _ => notified),
            ("LastNotifiedWeekAsync", _ => (DateOnly?)null),
            ("CollectAsync", args => new WeeklyReviewFacts((DateOnly)args[1]!, (string)args[2]!,
                WeeklyReviewComposer.BuildStats([], null, 0, 0, 0, 0), [], [], [])),
            ("SaveAsync", args =>
            {
                var facts = (WeeklyReviewFacts)args[1]!;
                onSave?.Invoke((string)args[2]!, (bool)args[3]!, (bool)args[4]!);
                onSaveFacts?.Invoke(facts, (bool)args[4]!);
                return (WeeklyReviewRecord?)new WeeklyReviewRecord(Guid.NewGuid(), facts.WeekStart, facts.WeekEnd,
                    (string)args[2]!, (bool)args[3]!, facts.Stats, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
            }));

    private sealed class FixedNarrator(string? text) : IWeeklyReviewNarrator
    {
        public Task<string?> NarrateAsync(Guid ownerId, WeeklyReviewFacts facts, CancellationToken cancellationToken) =>
            Task.FromResult(text);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public sealed class WeeklyReviewNarratorTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000beef");

    [Fact]
    public async Task Narrator_returns_the_model_paragraph()
    {
        var narrator = new WeeklyReviewNarrator(new FixedChatClientResolver(new TextClient("```\nSteady week.\n```")),
            new PersonaService(new InMemorySettingsStore()), NullLogger<WeeklyReviewNarrator>.Instance);

        var text = await narrator.NarrateAsync(Owner, WeeklyReviewComposerTests.Facts(
            WeeklyReviewComposer.BuildStats([], null, 0, 0, 0, 0)), CancellationToken.None);

        Assert.Equal("Steady week.", text);
    }

    [Fact]
    public async Task Narrator_failure_returns_null()
    {
        var narrator = new WeeklyReviewNarrator(new FixedChatClientResolver(new TextClient(null)),
            new PersonaService(new InMemorySettingsStore()), NullLogger<WeeklyReviewNarrator>.Instance);

        Assert.Null(await narrator.NarrateAsync(Owner, WeeklyReviewComposerTests.Facts(
            WeeklyReviewComposer.BuildStats([], null, 0, 0, 0, 0)), CancellationToken.None));
    }

    private sealed class TextClient(string? text) : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            text is null
                ? throw new InvalidOperationException("model down")
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
