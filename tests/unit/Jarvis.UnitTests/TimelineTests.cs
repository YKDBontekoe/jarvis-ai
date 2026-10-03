using Jarvis.Agents.Timeline;
using Jarvis.Application.Conversations;
using Jarvis.Application.Expenses;
using Jarvis.Application.Habits;
using Jarvis.Application.Journal;
using Jarvis.Application.People;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.People;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class TimelineTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly DateOnly Today = new(2026, 10, 3);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pearson_is_one_for_a_straight_line_and_null_without_variation()
    {
        Assert.Equal(1.0, TimelineInsightEngine.Pearson([(1, 2), (2, 4), (3, 6), (4, 8)])!.Value, 6);
        Assert.Equal(-1.0, TimelineInsightEngine.Pearson([(1, 8), (2, 6), (3, 4), (4, 2)])!.Value, 6);
        Assert.Null(TimelineInsightEngine.Pearson([(1, 5), (2, 5), (3, 5)]));
        Assert.Null(TimelineInsightEngine.Pearson([(1, 1)]));
    }

    [Fact]
    public void Habits_that_track_mood_become_an_insight()
    {
        var days = Enumerable.Range(0, 20).Select(i =>
        {
            var done = i % 2 == 0;
            return Day(Today.AddDays(-i), mood: done ? 4.5 : 2.0, habits: done ? 1.0 : 0.0);
        }).ToArray();

        var insights = TimelineInsightEngine.Analyze(days);

        var insight = Assert.Single(insights, x => x.Id == "corr:habits:mood");
        Assert.Contains("Mood is higher", insight.Headline);
        Assert.True(insight.Strength > 0.9);
    }

    [Fact]
    public void Too_few_days_or_flat_data_give_no_correlation_insights()
    {
        var few = Enumerable.Range(0, 4).Select(i => Day(Today.AddDays(-i), mood: i, habits: i)).ToArray();
        var flat = Enumerable.Range(0, 20).Select(i => Day(Today.AddDays(-i), mood: 3, habits: 1)).ToArray();

        Assert.DoesNotContain(TimelineInsightEngine.Analyze(few), x => x.Id.StartsWith("corr:", StringComparison.Ordinal));
        Assert.DoesNotContain(TimelineInsightEngine.Analyze(flat), x => x.Id.StartsWith("corr:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rising_mood_is_reported_as_a_trend()
    {
        var days = Enumerable.Range(0, 28).Select(i =>
            Day(Today.AddDays(-i), mood: i < 14 ? 4.5 : 3.0)).ToArray();

        var trend = Assert.Single(TimelineInsightEngine.Analyze(days), x => x.Id == "trend:mood");

        Assert.Equal("Mood is trending up", trend.Headline);
    }

    [Fact]
    public async Task Query_merges_sources_newest_first_and_filters_by_kind_and_text()
    {
        var service = CreateService(
            new StubSource(TimelineKinds.Journal, Event("a", TimelineKinds.Journal, "Journal entry", Today, 9)),
            new StubSource(TimelineKinds.Expense,
                Event("b", TimelineKinds.Expense, "Spent €12.00 at Bagels", Today, 13),
                Event("c", TimelineKinds.Expense, "Spent €4.00 at Kiosk", Today.AddDays(-1), 10)));

        var all = await service.QueryAsync(Owner, new TimelineQuery(Today.AddDays(-2), Today), default);
        var onlyExpenses = await service.QueryAsync(Owner, new TimelineQuery(Today.AddDays(-2), Today,
            new HashSet<string> { TimelineKinds.Expense }), default);
        var searched = await service.QueryAsync(Owner,
            new TimelineQuery(Today.AddDays(-2), Today, Text: "bagels"), default);

        Assert.Equal(3, all.Total);
        Assert.Equal(Today, all.Days[0].Date);
        Assert.Equal(["b", "a"], all.Days[0].Events.Select(x => x.Id.Split(':')[1]));
        Assert.Equal(2, onlyExpenses.Total);
        Assert.Single(searched.Days.SelectMany(x => x.Events));
    }

    [Fact]
    public async Task A_failing_source_is_reported_without_blanking_the_timeline()
    {
        var service = CreateService(
            new StubSource(TimelineKinds.Journal, Event("a", TimelineKinds.Journal, "Journal entry", Today, 9)),
            new ThrowingSource(TimelineKinds.Task));

        var result = await service.QueryAsync(Owner, new TimelineQuery(Today, Today), default);

        Assert.Equal(1, result.Total);
        Assert.Contains(result.Sources, x => x is { Kind: TimelineKinds.Task, Succeeded: false });
        Assert.Contains(result.Sources, x => x is { Kind: TimelineKinds.Journal, Succeeded: true });
    }

    [Fact]
    public async Task Limit_truncates_but_still_counts_everything()
    {
        var service = CreateService(new StubSource(TimelineKinds.Memory,
            Enumerable.Range(0, 5).Select(i => Event($"m{i}", TimelineKinds.Memory, $"Learned {i}", Today, i)).ToArray()));

        var result = await service.QueryAsync(Owner, new TimelineQuery(Today, Today, Limit: 2), default);

        Assert.Equal(5, result.Total);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Days.Sum(x => x.Events.Count));
    }

    [Fact]
    public void Windows_are_swapped_when_reversed_and_capped()
    {
        var (from, to) = TimelineService.Clamp(Today, Today.AddYears(-3));
        Assert.True(from < to);
        Assert.Equal(Today, to);
        Assert.Equal(TimelineRules.MaxWindowDays, to.DayNumber - from.DayNumber + 1);
    }

    [Fact]
    public async Task On_this_day_returns_earlier_years_only()
    {
        var service = CreateService(new StubSource(TimelineKinds.Journal,
            Event("y1", TimelineKinds.Journal, "Last year", Today.AddYears(-1), 9),
            Event("y2", TimelineKinds.Journal, "Two years ago", Today.AddYears(-2), 9),
            Event("other", TimelineKinds.Journal, "Another day", Today.AddYears(-1).AddDays(3), 9)));

        var years = await service.OnThisDayAsync(Owner, Today, 3, default);

        Assert.Equal([2025, 2024], years.Select(x => x.Year));
        Assert.All(years, year => Assert.Single(year.Events));
    }

    [Fact]
    public async Task Birthdays_land_on_the_28th_in_non_leap_years()
    {
        var person = new Person(Guid.NewGuid(), Owner, "Lena", "friend", 2, 29, 2000, null, null, null, null, null,
            null, Now, Now);
        var people = Fake<IPeopleRepository>.Create(("ListAsync", _ => (IReadOnlyList<Person>)[person]));
        var source = new PeopleTimelineSource(people);

        var events = await source.ListAsync(
            new TimelineWindow(Owner, new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), TimeZoneInfo.Utc, 100), default);

        var birthday = Assert.Single(events);
        Assert.Equal(new DateOnly(2026, 2, 28), birthday.Date);
        Assert.Equal("Lena's birthday", birthday.Title);
        Assert.Equal("turns 26", birthday.Detail);
    }

    [Fact]
    public async Task Expenses_show_the_amount_and_shop()
    {
        var expense = new Expense(Guid.NewGuid(), Owner, 12.5m, "EUR", "Bagels", "dining", "lunch", Today, null,
            "app", Now, Now);
        var repository = Fake<IExpenseRepository>.Create(("ListAsync", _ => (IReadOnlyList<Expense>)[expense]));

        var events = await new ExpenseTimelineSource(repository).ListAsync(
            new TimelineWindow(Owner, Today, Today, TimeZoneInfo.Utc, 100), default);

        var item = Assert.Single(events);
        Assert.Equal("Spent €12.50 at Bagels", item.Title);
        Assert.Equal(12.5m, item.Amount);
        Assert.Equal(TimelineKinds.Expense, item.Kind);
    }

    [Fact]
    public async Task The_agent_tool_marks_timeline_text_as_data_and_rejects_bad_input()
    {
        var service = CreateService(new StubSource(TimelineKinds.Journal,
            Event("a", TimelineKinds.Journal, "Journal entry", Today, 9)));
        var tools = new TimelineAgentTools(service, new FixedUser());

        var text = await tools.QueryTimelineAsync();
        var badDate = await tools.QueryTimelineAsync(from: "yesterday");
        var badKind = await tools.QueryTimelineAsync(kinds: "secrets");

        Assert.Contains("not instructions", text);
        Assert.Contains("[journal] Journal entry", text);
        Assert.Contains("YYYY-MM-DD", badDate);
        Assert.Contains("Unknown kind", badKind);
    }

    private static DayMetrics Day(DateOnly date, double? mood = null, double? habits = null) =>
        new(date, mood, null, null, null, 0m, habits, 0);

    private static TimelineEvent Event(string id, string kind, string title, DateOnly date, int hour) =>
        new($"{kind}:{id}", kind, title, null,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero), date);

    private static TimelineService CreateService(params ITimelineSource[] sources)
    {
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));
        return new TimelineService(sources, Fake<IJournalRepository>.Create(), Fake<IExpenseRepository>.Create(),
            Fake<IHabitRepository>.Create(), Fake<IJarvisTaskRepository>.Create(), briefings, new FixedClock());
    }

    private sealed class StubSource(string kind, params TimelineEvent[] events) : ITimelineSource
    {
        public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { kind };

        public Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TimelineEvent>>(events
                .Where(x => x.Date >= window.From && x.Date <= window.To).ToArray());
    }

    private sealed class ThrowingSource(string kind) : ITimelineSource
    {
        public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { kind };

        public Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
