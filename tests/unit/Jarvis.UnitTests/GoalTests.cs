using Jarvis.Agents.Goals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Goals;
using Jarvis.Application.Habits;
using Jarvis.Application.Inbox;
using Jarvis.Application.Planner;
using Jarvis.Application.Projects;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Habits;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class GoalTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000ee01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000ee02");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero); // a Saturday
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static GoalRecord Goal(string status = GoalStatuses.Active, int every = 7, int minutes = 90,
        DateOnly? horizon = null, IEnumerable<GoalCheckIn>? checkIns = null, DateOnly? dismissed = null,
        Dictionary<string, DateOnly>? declined = null, DateTimeOffset? created = null) =>
        new(Guid.NewGuid(), "Run a 10k", null, horizon, null, every, minutes, status, null, [], [],
            created ?? Now.AddDays(-30), Now, checkIns?.ToArray() ?? [], null, [],
            declined ?? new Dictionary<string, DateOnly>(), dismissed);

    private static GoalCheckIn Check(string feeling, int daysAgo, int season = 1) =>
        new(Guid.NewGuid(), Now.AddDays(-daysAgo), "Ran twice", null, null, feeling, season);

    [Theory]
    [InlineData("2026-10-17", "2026-10-12")]
    [InlineData("2026-10-12", "2026-10-12")]
    [InlineData("2026-10-18", "2026-10-12")]
    [InlineData("2026-10-19", "2026-10-19")]
    public void Weeks_start_on_monday(string day, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), GoalLogic.MondayOf(DateOnly.Parse(day)));

    [Fact]
    public void A_check_in_is_due_only_after_the_chosen_rhythm_and_never_for_a_paused_goal()
    {
        Assert.False(GoalLogic.CheckInDue(Goal(checkIns: [Check("on_track", 3)]), Today, Utc));
        Assert.True(GoalLogic.CheckInDue(Goal(checkIns: [Check("on_track", 7)]), Today, Utc));
        Assert.True(GoalLogic.CheckInDue(Goal(), Today, Utc));
        Assert.False(GoalLogic.CheckInDue(Goal(GoalStatuses.Paused, checkIns: [Check("on_track", 30)]), Today, Utc));
    }

    [Fact]
    public void A_new_goal_counts_from_the_day_it_began_not_from_the_beginning_of_time()
    {
        var fresh = Goal(created: Now.AddDays(-1));

        Assert.False(GoalLogic.CheckInDue(fresh, Today, Utc));
        Assert.Equal(1, GoalLogic.DaysSinceCheckIn(fresh, Today, Utc));
    }

    [Fact]
    public void Review_needs_two_check_ins_and_follows_what_the_owner_wrote()
    {
        Assert.Null(GoalLogic.Review(Goal(checkIns: [Check("stuck", 3)]), Today));
        Assert.Equal(GoalVerdicts.Keep,
            GoalLogic.Review(Goal(checkIns: [Check("on_track", 14), Check("on_track", 7)]), Today)!.Verdict);
        Assert.Equal(GoalVerdicts.Change,
            GoalLogic.Review(Goal(checkIns: [Check("stuck", 14), Check("on_track", 10), Check("stuck", 7)]), Today)!
                .Verdict);
        Assert.Equal(GoalVerdicts.Change,
            GoalLogic.Review(Goal(checkIns: [Check("slipping", 14), Check("stuck", 7)]), Today)!.Verdict);
        Assert.Null(GoalLogic.Review(Goal(checkIns: [Check("on_track", 14), Check("slipping", 7)]), Today));
    }

    [Fact]
    public void Review_only_uses_this_seasons_check_ins_and_names_them()
    {
        var old = Check("stuck", 40, season: 1);
        var goal = Goal(checkIns: [old, Check("on_track", 14, 2), Check("on_track", 7, 2)]) with { Season = 2 };

        var review = GoalLogic.Review(goal, Today)!;

        Assert.Equal(GoalVerdicts.Keep, review.Verdict);
        Assert.DoesNotContain(old.Id, review.BasedOn);
        Assert.Equal(2, review.BasedOn.Count);
    }

    [Fact]
    public void A_dismissed_review_stays_quiet_for_a_month_then_returns()
    {
        var checkIns = new[] { Check("stuck", 14), Check("stuck", 7) };

        Assert.Null(GoalLogic.Review(Goal(checkIns: checkIns, dismissed: Today.AddDays(-10)), Today));
        Assert.NotNull(GoalLogic.Review(Goal(checkIns: checkIns, dismissed: Today.AddDays(-28)), Today));
    }

    [Fact]
    public void A_passed_horizon_suggests_finishing_or_moving_it_and_a_paused_goal_gets_nothing()
    {
        Assert.Equal(GoalVerdicts.Stop, GoalLogic.Review(Goal(horizon: Today.AddDays(-1)), Today)!.Verdict);
        Assert.Null(GoalLogic.Review(Goal(GoalStatuses.Paused, horizon: Today.AddDays(-1)), Today));
    }

    private static PlanCandidate Candidate(string key, int minutes) => new(key, key, minutes, key);

    [Fact]
    public void A_plan_fits_the_minutes_set_aside_and_counts_what_did_not_fit()
    {
        var goal = Goal(minutes: 60);

        var plan = GoalLogic.Propose(goal, Today,
            [Candidate("a", 30), Candidate("b", 45), Candidate("c", 30)], []);

        Assert.Equal(["a", "c"], plan.Items.Select(item => item.Key));
        Assert.Equal(1, plan.OmittedForTime);
        Assert.Equal(new DateOnly(2026, 10, 12), plan.WeekStart);
        Assert.All(plan.Items, item => Assert.Equal(GoalPlanStatuses.Proposed, item.Status));
    }

    [Fact]
    public void A_plan_never_has_more_than_three_steps_and_skips_what_was_declined_lately()
    {
        var goal = Goal(minutes: 600, declined: new() { ["b"] = Today.AddDays(-5), ["old"] = Today.AddDays(-40) });

        var plan = GoalLogic.Propose(goal, Today,
            [Candidate("a", 10), Candidate("b", 10), Candidate("old", 10), Candidate("c", 10), Candidate("d", 10)], []);

        Assert.Equal(["a", "old", "c"], plan.Items.Select(item => item.Key));
    }

    [Fact]
    public void Accepted_steps_count_against_the_time_and_are_not_proposed_twice()
    {
        var accepted = new GoalPlanItem(Guid.NewGuid(), "a", 60, GoalPlanStatuses.Accepted, null, "a");

        var plan = GoalLogic.Propose(Goal(minutes: 90), Today, [Candidate("a", 60), Candidate("b", 45)], [accepted]);

        Assert.Equal(["a"], plan.Items.Select(item => item.Key));
        Assert.Equal(1, plan.OmittedForTime);
    }

    private sealed record Setup(GoalService Service, InMemorySettingsStore Settings, List<AddDayPlanItemRequest> Planned,
        Guid HabitId, Guid ProjectId, Commitment Promise);

    private static Setup Create(int thisWeek = 1, bool projectOpen = true)
    {
        var habitId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var habit = new Habit(habitId, Owner, "Run", null, HabitCadences.Weekly, 3, null, Now, Now);
        var summary = new HabitSummary(habit,
            new HabitStats(Today, 2, 4, false, thisWeek, 10, []));
        var promise = new Commitment(Guid.NewGuid(), Owner, CommitmentDirections.IOwe, "Anna", "Book the race",
            null, CommitmentStatuses.Open, false, CommitmentSources.Chat, null, null, null, Now, Now);
        var habitService = Fake<IHabitService>.Create(("GetAsync", args =>
            (Guid)args[0]! == habitId && (Guid)args[1]! == Owner ? summary : null));
        var project = new ProjectRecord(projectId, "Marathon", null, null, "blue", Now, Now, 0, 0, 0, "Finish a 10k");
        var store = Fake<IProjectStore>.Create(("GetAsync", args =>
            (Guid)args[0]! == projectId && (Guid)args[1]! == Owner ? project : null));
        var focus = Fake<IProjectFocusService>.Create(("DescribeAsync", _ => projectOpen
            ? new ProjectNextActionView(null, null, "Pick a training plan", null, ProjectNextActionStates.Open)
            : null));
        var promises = Fake<ICommitmentService>.Create(("GetAsync", args =>
            (Guid)args[0]! == promise.Id && (Guid)args[1]! == Owner ? promise : null));
        var planned = new List<AddDayPlanItemRequest>();
        var dayPlanner = Fake<IDayPlannerService>.Create(("AddItemAsync", args =>
        {
            planned.Add((AddDayPlanItemRequest)args[1]!);
            return new DayPlanItemDto(Guid.NewGuid(), "x", 30, false, null, null);
        }));
        var settings = new InMemorySettingsStore();
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));
        return new Setup(new GoalService(settings, habitService, store, focus, promises, dayPlanner, briefings,
            new FixedClock(Now)), settings, planned, habitId, projectId, promise);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static GoalInput Input(string title = "Run a 10k", Guid? project = null, IReadOnlyList<Guid>? habits = null,
        IReadOnlyList<Guid>? promises = null, int? minutes = null, string? status = null) =>
        new(title, "Feel fit", null, null, null, minutes, status, project, habits, promises);

    [Fact]
    public async Task A_goal_links_a_habit_a_project_and_a_promise_it_does_not_copy()
    {
        var s = Create();

        var created = await s.Service.CreateAsync(Owner,
            Input(project: s.ProjectId, habits: [s.HabitId], promises: [s.Promise.Id]), default);

        var view = created.Value!;
        Assert.Equal("Marathon", view.Project!.Name);
        Assert.Equal("Run", Assert.Single(view.Habits).Name);
        Assert.Equal("Book the race", Assert.Single(view.Commitments).Description);
        Assert.Contains(view.Progress.Measured, line => line.StartsWith("Run: 1 of 3"));
    }

    [Fact]
    public async Task Links_to_things_that_are_not_the_owners_are_refused_and_nothing_is_saved()
    {
        var s = Create();

        foreach (var input in new[]
                 {
                     Input(project: Guid.NewGuid()), Input(habits: [Guid.NewGuid()]), Input(promises: [Guid.NewGuid()])
                 })
            Assert.Equal(GoalFailure.Invalid, (await s.Service.CreateAsync(Owner, input, default)).Failure);
        Assert.Empty(await s.Service.ListAsync(Owner, true, default));
        Assert.Equal(GoalFailure.Invalid,
            (await s.Service.CreateAsync(Other, Input(habits: [s.HabitId]), default)).Failure);
    }

    [Fact]
    public async Task Bad_values_are_refused_with_the_field_that_caused_it()
    {
        var s = Create();

        Assert.Equal("title", (await s.Service.CreateAsync(Owner, Input("  "), default)).Field);
        Assert.Equal("weeklyMinutes", (await s.Service.CreateAsync(Owner, Input(minutes: 5), default)).Field);
        Assert.Equal("checkInEveryDays",
            (await s.Service.CreateAsync(Owner, Input() with { CheckInEveryDays = 1 }, default)).Field);
        Assert.Equal("status", (await s.Service.CreateAsync(Owner, Input(status: "x"), default)).Field);
        Assert.Equal("horizon",
            (await s.Service.CreateAsync(Owner, Input() with { Horizon = Today.AddDays(-1) }, default)).Field);
    }

    [Fact]
    public async Task Goals_are_per_owner()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(), default)).Value!.Goal;

        Assert.Null(await s.Service.GetAsync(Other, goal.Id, default));
        Assert.Empty(await s.Service.ListAsync(Other, true, default));
        Assert.Equal(GoalFailure.NotFound, (await s.Service.AddCheckInAsync(Other, goal.Id,
            new CheckInInput("x", null, null, null), default)).Failure);
        Assert.False(await s.Service.DeleteAsync(Other, goal.Id, default));
    }

    [Fact]
    public async Task A_goal_advances_on_check_ins_without_a_perfect_streak()
    {
        var s = Create(thisWeek: 0);
        var goal = (await s.Service.CreateAsync(Owner, Input(habits: [s.HabitId]), default)).Value!.Goal;

        var view = (await s.Service.AddCheckInAsync(Owner, goal.Id,
            new CheckInInput("Walked once, felt good", "Rainy week", "Try mornings", "slipping"), default)).Value!;

        var reflection = Assert.Single(view.Progress.Reflections);
        Assert.Equal("Walked once, felt good", reflection.Progress);
        Assert.Equal("Try mornings", reflection.Adjustment);
        Assert.False(view.CheckInDue);
        Assert.Null(view.Progress.Interpretation);
    }

    [Fact]
    public async Task A_check_in_needs_progress_and_a_known_feeling()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(), default)).Value!.Goal;

        Assert.Equal("progress", (await s.Service.AddCheckInAsync(Owner, goal.Id,
            new CheckInInput(" ", null, null, null), default)).Field);
        Assert.Equal("feeling", (await s.Service.AddCheckInAsync(Owner, goal.Id,
            new CheckInInput("ok", null, null, "great"), default)).Field);
    }

    [Fact]
    public async Task The_weekly_plan_proposes_the_projects_next_step_the_promise_and_the_habit_within_the_time()
    {
        var s = Create(thisWeek: 1);
        var goal = (await s.Service.CreateAsync(Owner,
            Input(project: s.ProjectId, habits: [s.HabitId], promises: [s.Promise.Id], minutes: 90), default)).Value!.Goal;

        var plan = (await s.Service.ProposePlanAsync(Owner, goal.Id, default)).Value!.Goal.Plan!;

        Assert.Equal(["Pick a training plan", "Book the race"], plan.Items.Select(item => item.Title));
        Assert.Equal(60, plan.Items.Sum(item => item.Minutes));
        Assert.Equal(1, plan.OmittedForTime); // the habit's two sessions (40 min) do not fit the 30 min left
        Assert.Equal($"commitment:{s.Promise.Id}", plan.Items[1].Ref);
    }

    [Fact]
    public async Task Accept_reduce_decline_and_done_change_one_step_and_declining_is_remembered()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner,
            Input(project: s.ProjectId, promises: [s.Promise.Id]), default)).Value!.Goal;
        var plan = (await s.Service.ProposePlanAsync(Owner, goal.Id, default)).Value!.Goal.Plan!;
        var first = plan.Items[0];
        var second = plan.Items[1];

        var accepted = await s.Service.ActOnPlanItemAsync(Owner, goal.Id, first.Id, new("accept", null), null, default);
        Assert.Equal(GoalPlanStatuses.Accepted, accepted.Value!.Goal.Plan!.Items[0].Status);
        var reduced = await s.Service.ActOnPlanItemAsync(Owner, goal.Id, first.Id, new("reduce", 10), null, default);
        Assert.Equal(10, reduced.Value!.Goal.Plan!.Items[0].Minutes);
        var declined = await s.Service.ActOnPlanItemAsync(Owner, goal.Id, second.Id, new("decline", null), null, default);
        Assert.True(declined.Value!.Goal.Declined.ContainsKey(second.Key));

        var again = (await s.Service.ProposePlanAsync(Owner, goal.Id, default)).Value!.Goal.Plan!;
        Assert.DoesNotContain(again.Items, item => item.Key == second.Key && item.Status == GoalPlanStatuses.Proposed);
        Assert.Contains(again.Items, item => item.Id == first.Id && item.Status == GoalPlanStatuses.Accepted);
    }

    [Fact]
    public async Task Reducing_must_make_the_step_shorter_and_actions_are_checked()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(project: s.ProjectId), default)).Value!.Goal;
        var item = (await s.Service.ProposePlanAsync(Owner, goal.Id, default)).Value!.Goal.Plan!.Items[0];

        Assert.Equal(GoalFailure.Invalid,
            (await s.Service.ActOnPlanItemAsync(Owner, goal.Id, item.Id, new("reduce", 60), null, default)).Failure);
        Assert.Equal(GoalFailure.Invalid,
            (await s.Service.ActOnPlanItemAsync(Owner, goal.Id, item.Id, new("explode", null), null, default)).Failure);
        Assert.Equal(GoalFailure.NotFound,
            (await s.Service.ActOnPlanItemAsync(Owner, goal.Id, Guid.NewGuid(), new("accept", null), null, default)).Failure);
    }

    [Fact]
    public async Task Only_an_accepted_step_can_go_on_the_day_plan_and_a_promise_keeps_its_source()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(promises: [s.Promise.Id]), default)).Value!.Goal;
        var item = (await s.Service.ProposePlanAsync(Owner, goal.Id, default)).Value!.Goal.Plan!.Items[0];

        var early = await s.Service.ActOnPlanItemAsync(Owner, goal.Id, item.Id, new("schedule", null), null, default);
        Assert.Equal(GoalFailure.Conflict, early.Failure);
        Assert.Empty(s.Planned);

        await s.Service.ActOnPlanItemAsync(Owner, goal.Id, item.Id, new("accept", null), null, default);
        var scheduled = await s.Service.ActOnPlanItemAsync(Owner, goal.Id, item.Id, new("schedule", null), "UTC", default);

        Assert.True(scheduled.Succeeded);
        var request = Assert.Single(s.Planned);
        Assert.Equal("Book the race", request.Title);
        Assert.Equal($"commitment:{s.Promise.Id}", request.SourceRef);
    }

    [Fact]
    public async Task A_paused_goal_does_not_plan_and_pause_and_archive_keep_history()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(), default)).Value!.Goal;
        await s.Service.AddCheckInAsync(Owner, goal.Id, new CheckInInput("Started", null, null, null), default);

        var paused = await s.Service.UpdateAsync(Owner, goal.Id, Input() with { Status = GoalStatuses.Paused }, default);

        Assert.Single(paused.Value!.Goal.CheckIns);
        Assert.Equal(GoalFailure.Conflict, (await s.Service.ProposePlanAsync(Owner, goal.Id, default)).Failure);
        Assert.Single(await s.Service.ListAsync(Owner, false, default));
        await s.Service.UpdateAsync(Owner, goal.Id, Input() with { Status = GoalStatuses.Archived }, default);
        Assert.Empty(await s.Service.ListAsync(Owner, false, default));
        Assert.Single(await s.Service.ListAsync(Owner, true, default));
    }

    [Fact]
    public async Task Resetting_the_season_starts_fresh_progress_and_keeps_the_old_check_ins()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(), default)).Value!.Goal;
        await s.Service.AddCheckInAsync(Owner, goal.Id, new CheckInInput("Season one", null, null, "stuck"), default);

        var reset = (await s.Service.ResetSeasonAsync(Owner, goal.Id, default)).Value!;

        Assert.Equal(2, reset.Goal.Season);
        Assert.Single(reset.Goal.CheckIns);
        Assert.Empty(reset.Progress.Reflections);
        Assert.Null(reset.Goal.Plan);
    }

    [Fact]
    public async Task Dismissing_a_suggestion_quiets_it_in_the_list_too()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(), default)).Value!.Goal;
        await s.Service.AddCheckInAsync(Owner, goal.Id, new CheckInInput("a", null, null, "stuck"), default);
        await s.Service.AddCheckInAsync(Owner, goal.Id, new CheckInInput("b", null, null, "stuck"), default);
        Assert.NotNull((await s.Service.ListAsync(Owner, false, default)).Single().Review);

        await s.Service.DismissReviewAsync(Owner, goal.Id, default);

        Assert.Null((await s.Service.ListAsync(Owner, false, default)).Single().Review);
    }

    [Fact]
    public async Task One_experiment_at_a_time_with_notes_as_observations_and_a_conclusion()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(), default)).Value!.Goal;

        var started = (await s.Service.StartExperimentAsync(Owner, goal.Id,
            new ExperimentInput("Run before breakfast", "More energy", 14), default)).Value!.Goal.Experiments.Single();
        Assert.Equal(Today.AddDays(14), started.ReviewOn);
        Assert.Equal(GoalFailure.Conflict, (await s.Service.StartExperimentAsync(Owner, goal.Id,
            new ExperimentInput("Another", "x", 7), default)).Failure);

        var noted = await s.Service.NoteExperimentAsync(Owner, goal.Id, started.Id, "Slept badly, ran anyway", default);
        Assert.Single(noted.Value!.Goal.Experiments.Single().Notes);
        var finished = (await s.Service.FinishExperimentAsync(Owner, goal.Id, started.Id, true, "Keep it", default))
            .Value!.Goal.Experiments.Single();
        Assert.Equal(ExperimentStatuses.Kept, finished.Status);
        Assert.Equal(GoalFailure.Conflict,
            (await s.Service.NoteExperimentAsync(Owner, goal.Id, started.Id, "late", default)).Failure);
        Assert.Equal("durationDays", (await s.Service.StartExperimentAsync(Owner, goal.Id,
            new ExperimentInput("a", "b", 1), default)).Field);
    }

    [Fact]
    public async Task The_tools_record_check_ins_and_only_propose_plans()
    {
        var s = Create();
        var goal = (await s.Service.CreateAsync(Owner, Input(project: s.ProjectId), default)).Value!.Goal;
        var tools = new GoalAgentTools(s.Service, new FixedUser());

        var saved = await tools.AddGoalCheckInAsync(goal.Id.ToString(), "Ran 5k", feeling: "on_track");
        var plan = await tools.ProposeGoalPlanAsync(goal.Id.ToString());
        var list = await tools.GetGoalsAsync();

        Assert.Contains("Check-in saved", saved);
        Assert.Contains("not accepted yet", plan);
        Assert.Empty(s.Planned);
        Assert.Contains("not instructions", list);
        Assert.Contains("I could not find that goal", await tools.AddGoalCheckInAsync(Guid.NewGuid().ToString(), "x"));
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
