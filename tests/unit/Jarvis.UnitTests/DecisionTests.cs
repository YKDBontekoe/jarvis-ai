using Jarvis.Application.Decisions;
using Jarvis.Application.Reviews;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Decisions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DecisionTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000dddd");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000eeee");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 5);

    private static ResolvedPrediction P(double probability, bool outcome, int daysAgo = 0) =>
        new(probability, outcome, Now.AddDays(-daysAgo));

    // ---- calibration maths ----

    [Fact]
    public void Brier_is_zero_for_perfect_and_a_quarter_for_always_fifty_percent()
    {
        Assert.Equal(0.0, CalibrationCalculator.Brier([P(1.0, true), P(0.0, false)])!.Value, 6);
        Assert.Equal(0.25, CalibrationCalculator.Brier([P(0.5, true), P(0.5, false)])!.Value, 6);
        Assert.Equal(1.0, CalibrationCalculator.Brier([P(0.0, true)])!.Value, 6);
        Assert.Null(CalibrationCalculator.Brier([]));
    }

    [Fact]
    public void Report_groups_predictions_by_stated_confidence()
    {
        var report = CalibrationCalculator.Report(
        [
            P(0.2, false), P(0.2, false), P(0.2, true),
            P(0.7, true), P(0.7, true), P(0.7, false),
            P(0.95, true)
        ]);

        Assert.Equal(7, report.Resolved);
        Assert.Equal(5, report.Buckets.Count);
        var low = report.Buckets[0];
        Assert.Equal(3, low.Count);
        Assert.Equal(0.333, low.ActualRate!.Value, 3);
        var high = report.Buckets[3];
        Assert.Equal(3, high.Count);
        Assert.Equal(0.667, high.ActualRate!.Value, 3);
        Assert.Equal(0, report.Buckets[1].Count);
        Assert.Null(report.Buckets[1].ActualRate);
        Assert.Equal(1, report.Buckets[4].Count);
    }

    [Fact]
    public void Bucket_edges_belong_to_the_higher_bucket()
    {
        var report = CalibrationCalculator.Report([P(0.30, true), P(0.50, true), P(0.70, true), P(0.90, true)]);

        Assert.Equal([0, 1, 1, 1, 1], report.Buckets.Select(b => b.Count).ToArray());
    }

    [Fact]
    public void An_empty_history_has_no_scores_but_still_five_buckets()
    {
        var report = CalibrationCalculator.Report([]);

        Assert.Equal(0, report.Resolved);
        Assert.Null(report.Brier);
        Assert.Null(report.Trend);
        Assert.Equal(5, report.Buckets.Count);
    }

    [Fact]
    public void Trend_compares_the_latest_ten_with_the_ten_before()
    {
        // Older calls were confidently wrong, recent ones are right.
        var history = Enumerable.Range(0, 10).Select(i => P(0.9, true, i))
            .Concat(Enumerable.Range(10, 10).Select(i => P(0.9, false, i))).ToArray();

        var report = CalibrationCalculator.Report(history);

        Assert.Equal("improving", report.Trend);
        Assert.True(report.RecentBrier < report.PreviousBrier);

        var worse = CalibrationCalculator.Report(
            Enumerable.Range(0, 10).Select(i => P(0.9, false, i))
                .Concat(Enumerable.Range(10, 10).Select(i => P(0.9, true, i))).ToArray());
        Assert.Equal("worsening", worse.Trend);

        var steady = CalibrationCalculator.Report(
            Enumerable.Range(0, 20).Select(i => P(0.7, i % 2 == 0, i)).ToArray());
        Assert.Equal("steady", steady.Trend);
    }

    [Fact]
    public void A_trend_needs_enough_answers_on_both_sides()
    {
        Assert.Null(CalibrationCalculator.Report(Enumerable.Range(0, 8).Select(i => P(0.9, true, i)).ToArray()).Trend);
        Assert.Null(CalibrationCalculator.Report(Enumerable.Range(0, 12).Select(i => P(0.9, true, i)).ToArray()).Trend);
    }

    // ---- service ----

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new(Now);
        public InMemoryDecisions Repository { get; } = new();
        public RecordingReminders Reminders { get; } = new();
        public DecisionService Service { get; }

        public Harness() => Service = new DecisionService(Repository, Reminders, new NoBriefings(), Clock);

        public Task<DecisionView> Log(Guid? owner = null, double probability = 0.7, int reviewInDays = 7,
            string title = "Ship by Friday") =>
            Service.CreateAsync(owner ?? Owner,
                new CreateDecisionRequest(title, "We ship on time", probability, Today.AddDays(reviewInDays)),
                CancellationToken.None);
    }

    [Fact]
    public async Task Logging_a_decision_schedules_a_reminder_at_nine_on_the_review_date()
    {
        var h = new Harness();

        var view = await h.Log(reviewInDays: 7);

        Assert.Equal(DecisionStatuses.Open, view.Status);
        var reminder = Assert.Single(h.Reminders.Created);
        Assert.Equal(Owner, reminder.OwnerId);
        Assert.Equal("Check outcome: Ship by Friday", reminder.Request.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), reminder.Request.DueAt);
        Assert.Equal(h.Reminders.LastCreatedId, (await h.Repository.GetAsync(view.Id, Owner, default))!.ReminderId);
    }

    [Fact]
    public async Task A_review_date_today_after_nine_reminds_shortly_instead_of_in_the_past()
    {
        var h = new Harness();

        await h.Log(reviewInDays: 0);

        Assert.Equal(Now.AddMinutes(15), Assert.Single(h.Reminders.Created).Request.DueAt);
    }

    [Fact]
    public async Task A_decision_is_still_saved_when_the_reminder_cannot_be_created()
    {
        var h = new Harness { };
        h.Reminders.Fail = true;

        var view = await h.Log();

        Assert.Null((await h.Repository.GetAsync(view.Id, Owner, default))!.ReminderId);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.005)]
    [InlineData(1.0)]
    [InlineData(0.995)]
    [InlineData(double.NaN)]
    public async Task Probabilities_outside_one_to_ninety_nine_percent_are_rejected(double probability)
    {
        var h = new Harness();

        await Assert.ThrowsAsync<ArgumentException>(() => h.Log(probability: probability));
        Assert.Empty(h.Reminders.Created);
    }

    [Fact]
    public async Task Bad_titles_predictions_and_dates_are_rejected()
    {
        var h = new Harness();

        await Assert.ThrowsAsync<ArgumentException>(() => h.Log(title: "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Log(title: new string('x', 201)));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Log(reviewInDays: -1));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Log(reviewInDays: 701));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.CreateAsync(Owner,
            new CreateDecisionRequest("t", "  ", 0.5, Today.AddDays(1)), default));
    }

    [Fact]
    public async Task Status_follows_the_review_date_and_the_outcome()
    {
        var h = new Harness();
        var open = await h.Log(reviewInDays: 3, title: "later");
        var due = await h.Log(reviewInDays: 0, title: "today");

        Assert.Equal(DecisionStatuses.Open, open.Status);
        Assert.Equal(DecisionStatuses.Due, due.Status);
        h.Clock.Advance(TimeSpan.FromDays(4));
        Assert.All(await h.Service.ListAsync(Owner, DecisionStatuses.Due, 50, default),
            d => Assert.Equal(DecisionStatuses.Due, d.Status));
        Assert.Equal(2, (await h.Service.ListAsync(Owner, DecisionStatuses.Due, 50, default)).Count);

        var resolved = await h.Service.ResolveAsync(open.Id, Owner, true, "yes", default);

        Assert.Equal(DecisionStatuses.Resolved, resolved!.Status);
        Assert.Single(await h.Service.ListAsync(Owner, DecisionStatuses.Resolved, 50, default));
        Assert.Single(await h.Service.ListAsync(Owner, DecisionStatuses.Due, 50, default));
    }

    [Fact]
    public async Task Resolving_cancels_the_pending_reminder_and_stores_the_outcome()
    {
        var h = new Harness();
        var view = await h.Log();

        var resolved = await h.Service.ResolveAsync(view.Id, Owner, false, "  slipped a day  ", default);

        Assert.False(resolved!.Outcome);
        Assert.Equal("slipped a day", resolved.OutcomeNote);
        Assert.Equal(Now, resolved.ResolvedAt);
        Assert.Equal([h.Reminders.LastCreatedId], h.Reminders.Cancelled);
        Assert.Null((await h.Repository.GetAsync(view.Id, Owner, default))!.ReminderId);
    }

    [Fact]
    public async Task A_resolved_decision_can_be_re_answered_but_not_edited()
    {
        var h = new Harness();
        var view = await h.Log();
        await h.Service.ResolveAsync(view.Id, Owner, true, null, default);

        var again = await h.Service.ResolveAsync(view.Id, Owner, false, null, default);

        Assert.False(again!.Outcome);
        Assert.Single(h.Reminders.Cancelled);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.UpdateAsync(view.Id, Owner,
            new UpdateDecisionRequest(Title: "changed"), default));
    }

    [Fact]
    public async Task Changing_the_review_date_moves_the_reminder()
    {
        var h = new Harness();
        var view = await h.Log(reviewInDays: 7);
        var first = h.Reminders.LastCreatedId;

        var updated = await h.Service.UpdateAsync(view.Id, Owner,
            new UpdateDecisionRequest(ReviewOn: Today.AddDays(14), Probability: 0.8), default);

        Assert.Equal(Today.AddDays(14), updated!.ReviewOn);
        Assert.Equal(0.8, updated.Probability);
        Assert.Equal([first], h.Reminders.Cancelled);
        Assert.Equal(2, h.Reminders.Created.Count);
        Assert.Equal(h.Reminders.LastCreatedId, (await h.Repository.GetAsync(view.Id, Owner, default))!.ReminderId);
    }

    [Fact]
    public async Task Deleting_cancels_the_reminder()
    {
        var h = new Harness();
        var view = await h.Log();

        Assert.True(await h.Service.DeleteAsync(view.Id, Owner, default));

        Assert.Equal([h.Reminders.LastCreatedId], h.Reminders.Cancelled);
        Assert.Null(await h.Service.GetAsync(view.Id, Owner, default));
    }

    [Fact]
    public async Task Decisions_belong_to_one_owner()
    {
        var h = new Harness();
        var view = await h.Log();

        Assert.Null(await h.Service.GetAsync(view.Id, Other, default));
        Assert.Null(await h.Service.ResolveAsync(view.Id, Other, true, null, default));
        Assert.Null(await h.Service.UpdateAsync(view.Id, Other, new UpdateDecisionRequest(Title: "x"), default));
        Assert.False(await h.Service.DeleteAsync(view.Id, Other, default));
        Assert.Empty(await h.Service.ListAsync(Other, null, 50, default));
        Assert.Equal(0, (await h.Service.CalibrationAsync(Other, default)).Resolved);
        Assert.Empty(h.Reminders.Cancelled);
        Assert.NotNull(await h.Service.GetAsync(view.Id, Owner, default));
    }

    [Fact]
    public async Task Calibration_scores_only_resolved_decisions()
    {
        var h = new Harness();
        var a = await h.Log(probability: 0.9, title: "a");
        var b = await h.Log(probability: 0.9, title: "b");
        await h.Log(probability: 0.1, title: "still open");
        await h.Service.ResolveAsync(a.Id, Owner, true, null, default);
        await h.Service.ResolveAsync(b.Id, Owner, false, null, default);

        var report = await h.Service.CalibrationAsync(Owner, default);

        Assert.Equal(2, report.Resolved);
        Assert.Equal(0.41, report.Brier!.Value, 3);
        Assert.Equal(0.5, report.HitRate!.Value, 3);
    }

    [Fact]
    public async Task Listing_rejects_an_unknown_status_and_caps_the_page()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) await h.Log(title: $"d{i}");

        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.ListAsync(Owner, "bogus", 10, default));
        Assert.Single(await h.Service.ListAsync(Owner, null, 1, default));
        Assert.Equal(3, (await h.Service.ListAsync(Owner, null, 0 + 500, default)).Count);
    }

    [Fact]
    public async Task There_is_a_cap_on_unresolved_decisions()
    {
        var h = new Harness();
        for (var i = 0; i < DecisionRules.MaxUnresolved; i++) await h.Log(title: $"d{i}");

        await Assert.ThrowsAsync<ArgumentException>(() => h.Log(title: "one too many"));
    }

    // ---- timeline ----

    [Fact]
    public async Task The_timeline_shows_when_a_decision_was_logged_and_when_it_was_resolved()
    {
        var h = new Harness();
        var view = await h.Log(probability: 0.6);
        h.Clock.Advance(TimeSpan.FromDays(1));
        await h.Service.ResolveAsync(view.Id, Owner, true, "went fine", default);
        var source = new DecisionTimelineSource(h.Repository);

        var events = await source.ListAsync(
            new TimelineWindow(Owner, Today.AddDays(-1), Today.AddDays(3), TimeZoneInfo.Utc, 100), default);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(TimelineKinds.Decision, e.Kind));
        Assert.Contains(events, e => e.Detail!.Contains("60% sure"));
        Assert.Contains(events, e => e.Detail!.Contains("It happened") && e.Detail.Contains("went fine"));
        Assert.Empty(await source.ListAsync(
            new TimelineWindow(Other, Today.AddDays(-1), Today.AddDays(3), TimeZoneInfo.Utc, 100), default));
    }

    // ---- weekly review ----

    [Fact]
    public void The_weekly_review_says_how_predictions_scored_against_before()
    {
        var better = Stats(3, 0.12, 0.30);
        var worse = Stats(2, 0.40, 0.10);
        var same = Stats(1, 0.20, 0.21);
        var first = Stats(1, 0.20, null);

        Assert.Contains("better than before", WeeklyReviewComposer.DescribeDecisions(better));
        Assert.Contains("worse than before", WeeklyReviewComposer.DescribeDecisions(worse));
        Assert.Contains("in line with before", WeeklyReviewComposer.DescribeDecisions(same));
        var firstText = WeeklyReviewComposer.DescribeDecisions(first)!;
        Assert.Contains("scored 0.20", firstText);
        Assert.DoesNotContain("before", firstText);
        Assert.Null(WeeklyReviewComposer.DescribeDecisions(Stats(0, null, null)));
    }

    [Fact]
    public void A_week_with_only_decisions_is_not_called_quiet()
    {
        var facts = new WeeklyReviewFacts(new DateOnly(2026, 9, 28), "UTC", Stats(2, 0.2, null), [], [], []);

        var story = WeeklyReviewComposer.Compose(facts);

        Assert.DoesNotContain("quiet week", story);
        Assert.Contains("settled 2 decisions", story);
    }

    [Fact]
    public void Old_weekly_review_stats_without_decision_fields_still_read_back()
    {
        const string old = """
            {"journalEntries":1,"mood":4,"energy":null,"stress":null,"rating":null,"previousMood":null,
             "bestDay":null,"bestDayRating":null,"tasksCompleted":0,"remindersHandled":0,"remindersUpcoming":0,
             "newMemories":0,"topTags":[],"days":[]}
            """;

        var stats = System.Text.Json.JsonSerializer.Deserialize<WeeklyReviewStats>(old,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(stats);
        Assert.Equal(0, stats.DecisionsResolved);
        Assert.Null(stats.BrierScore);
    }

    private static WeeklyReviewStats Stats(int resolved, double? brier, double? previous) =>
        WeeklyReviewComposer.BuildStats([], null, 0, 0, 0, 0, resolved, brier, previous);

    // ---- fakes ----

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class InMemoryDecisions : IDecisionRepository
    {
        private readonly List<Decision> _rows = [];

        public Task<IReadOnlyList<Decision>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Decision>>(_rows.Where(x => x.OwnerId == ownerId)
                .OrderByDescending(x => x.ReviewOn).ToArray());

        public Task<Decision?> GetAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_rows.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<int> CountUnresolvedAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_rows.Count(x => x.OwnerId == ownerId && x.Outcome is null));

        public Task AddAsync(Decision decision, CancellationToken ct)
        {
            _rows.Add(decision);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Decision decision, CancellationToken ct)
        {
            var index = _rows.FindIndex(x => x.Id == decision.Id && x.OwnerId == decision.OwnerId);
            if (index < 0) return Task.FromResult(false);
            _rows[index] = decision;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_rows.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<IReadOnlyList<Decision>> ListActiveBetweenAsync(Guid ownerId, DateTimeOffset from,
            DateTimeOffset to, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Decision>>(_rows.Where(x => x.OwnerId == ownerId &&
                ((x.CreatedAt >= from && x.CreatedAt < to) || (x.ResolvedAt >= from && x.ResolvedAt < to))).ToArray());
    }

    private sealed class RecordingReminders : IReminderService
    {
        public List<(Guid OwnerId, CreateReminderRequest Request)> Created { get; } = [];
        public List<Guid> Cancelled { get; } = [];
        public Guid LastCreatedId { get; private set; }
        public bool Fail { get; set; }

        public Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request, CancellationToken ct)
        {
            if (Fail) throw new ArgumentOutOfRangeException(nameof(request), "no");
            Created.Add((ownerId, request));
            LastCreatedId = Guid.NewGuid();
            return Task.FromResult(new ReminderRecord(LastCreatedId, ownerId, request.Title, request.DueAt, "wf",
                "pending", Now, null));
        }

        public Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken ct)
        {
            Cancelled.Add(id);
            return Task.FromResult<ReminderRecord?>(null);
        }

        public Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ReminderRecord>> ListAsync(Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ReminderRecord?> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset dueAt, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ReminderRecord?> MarkDoneAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NoBriefings : IDailyBriefingRepository
    {
        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<DailyBriefingPreferenceRecord?>(null);
        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken ct) => throw new NotSupportedException();
        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
