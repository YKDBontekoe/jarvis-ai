using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.Projects;
using Jarvis.Application.Reviews;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WeekFollowThroughTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    // Saturday 17 October 2026: the week is Mon 12 – Sun 18 October.
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateOnly WeekStart = new(2026, 10, 12);
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int day, int hour = 12) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    private static Commitment Promise(string what, string status = CommitmentStatuses.Open, DateOnly? due = null,
        DateTimeOffset? completed = null, string direction = CommitmentDirections.IOwe, bool suggested = false) =>
        new(Guid.CreateVersion7(), Owner, direction, "Rosa", what, due, status, suggested, CommitmentSources.Chat, null,
            null, completed, Created, Created);

    private static DecisionView Choice(string title, DateOnly reviewOn, string status = "open") =>
        new(Guid.CreateVersion7(), title, null, "It works", .7, reviewOn, status, null, null, null, Created);

    private static WeekProject Project(string name, ProjectNextActionView? next = null, string status = "active") =>
        new(Guid.CreateVersion7(), name, status, next);

    private static WeekFollowThrough Build(IReadOnlyList<Commitment>? commitments = null,
        IReadOnlyList<DecisionView>? decisions = null, IReadOnlyList<WeekProject>? projects = null,
        IReadOnlySet<string>? dismissed = null, TimeZoneInfo? zone = null) =>
        WeekFollowThroughBuilder.Build(new FollowThroughInputs(WeekStart, Today, zone ?? TimeZoneInfo.Utc,
            commitments ?? [], decisions ?? [], projects ?? [], dismissed ?? new HashSet<string>()));

    [Fact]
    public void Counts_reconcile_to_the_records_fulfilled_dropped_overdue_and_stale()
    {
        var result = Build([
            Promise("Sent quote", CommitmentStatuses.Done, completed: At(14)),
            Promise("Sent invoice", CommitmentStatuses.Done, completed: At(18, 23)),
            Promise("Done last week", CommitmentStatuses.Done, completed: At(9)),
            Promise("Dropped one", CommitmentStatuses.Dropped, completed: At(15)),
            Promise("Overdue 2 days", due: Today.AddDays(-2)),
            Promise("Overdue 9 days", due: Today.AddDays(-9)),
            Promise("Due later", due: Today.AddDays(3)),
            Promise("Not accepted", due: Today.AddDays(-30), suggested: true)
        ]);

        Assert.Equal(2, result.Fulfilled);
        Assert.Equal(1, result.Dropped);
        Assert.Equal(2, result.Overdue);
        Assert.Equal(1, result.Stale);
        Assert.Equal(["Sent quote", "Sent invoice"], result.FulfilledItems.Select(item => item.Title));
        Assert.Equal("Overdue 9 days", Assert.Single(result.StaleItems).Title);
        Assert.Equal("9 days overdue", result.StaleItems[0].Detail);
    }

    [Fact]
    public void The_week_is_the_owners_local_week_not_utc()
    {
        // 23:30 Sunday UTC is already Monday 00:30 in Amsterdam, so it belongs to the next week there.
        var late = Promise("Late finish", CommitmentStatuses.Done, completed: new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero));
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

        Assert.Equal(1, Build([late]).Fulfilled);
        Assert.Equal(0, Build([late], zone: amsterdam).Fulfilled);
    }

    [Fact]
    public void Decisions_due_next_week_are_listed_and_resolved_or_later_ones_are_not()
    {
        var result = Build(decisions: [
            Choice("Due Monday", new DateOnly(2026, 10, 19)),
            Choice("Due next Sunday", new DateOnly(2026, 10, 25)),
            Choice("Too late", new DateOnly(2026, 10, 26)),
            Choice("This week", new DateOnly(2026, 10, 14)),
            Choice("Resolved", new DateOnly(2026, 10, 20), "resolved")
        ]);

        Assert.Equal(2, result.DecisionsDueNextWeek);
        Assert.Equal(["Due Monday", "Due next Sunday"], result.NextWeekDecisions.Select(item => item.Title));
        Assert.StartsWith("decision:", result.NextWeekDecisions[0].Ref);
    }

    [Fact]
    public void Projects_with_no_next_step_or_a_finished_one_need_a_step_and_archived_ones_do_not()
    {
        var open = new ProjectNextActionView("commitment", Guid.CreateVersion7(), null, "Order tiles", ProjectNextActionStates.Open);
        var done = new ProjectNextActionView("commitment", Guid.CreateVersion7(), null, "Done thing", ProjectNextActionStates.Done);

        var result = Build(projects: [Project("Kitchen"), Project("Garden", open), Project("Taxes", done), Project("Old", status: "archived")]);

        Assert.Equal(["Kitchen", "Taxes"], result.ProjectsNeedingStep.Select(item => item.Title));
        Assert.Equal(["No next step", "Last step done"], result.ProjectsNeedingStep.Select(item => item.Detail));
    }

    [Fact]
    public void Suggestions_are_few_ordered_and_start_on_a_day_the_owner_can_change()
    {
        var first = Promise("Overdue 9 days", due: Today.AddDays(-9));
        var second = Promise("Overdue 2 days", due: Today.AddDays(-2));
        var theirs = Promise("They owe me", due: Today.AddDays(-20), direction: CommitmentDirections.OwedToMe);

        var result = Build([second, first, theirs], projects: [Project("Kitchen")]);

        Assert.Equal(3, result.Suggestions.Count);
        Assert.Equal(FollowThroughKinds.PlanCommitment, result.Suggestions[0].Kind);
        Assert.Equal(first.Id.ToString("D"), result.Suggestions[0].TargetId);
        Assert.Equal(new DateOnly(2026, 10, 19), result.Suggestions[0].SuggestedOn);
        Assert.Equal(FollowThroughKinds.ProjectNextStep, result.Suggestions[1].Kind);
        Assert.Null(result.Suggestions[1].SuggestedOn);
        Assert.Equal(second.Id.ToString("D"), result.Suggestions[2].TargetId);
        Assert.DoesNotContain(result.Suggestions, suggestion => suggestion.TargetId == theirs.Id.ToString("D"));
    }

    [Fact]
    public void A_dismissed_suggestion_does_not_come_back_and_the_next_one_takes_its_place()
    {
        var first = Promise("Overdue 9 days", due: Today.AddDays(-9));
        var second = Promise("Overdue 2 days", due: Today.AddDays(-2));
        var dismissed = new HashSet<string> { WeekFollowThroughBuilder.Fingerprint(FollowThroughKinds.PlanCommitment, first.Id.ToString("D")) };

        var result = Build([first, second], dismissed: dismissed);

        Assert.Equal(second.Id.ToString("D"), Assert.Single(result.Suggestions).TargetId);
    }

    [Fact]
    public void A_quiet_week_is_short_and_honest_with_no_invented_work()
    {
        var result = Build();

        Assert.Equal((0, 0, 0, 0, 0), (result.Fulfilled, result.Dropped, result.Overdue, result.Stale, result.DecisionsDueNextWeek));
        Assert.Empty(result.Suggestions);
        Assert.Empty(result.FulfilledItems);
        Assert.Empty(result.ProjectsNeedingStep);
    }

    [Fact]
    public void Listed_items_are_bounded()
    {
        var many = Enumerable.Range(0, 12).Select(i => Promise($"Done {i}", CommitmentStatuses.Done, completed: At(14, i))).ToArray();
        var result = Build(many);

        Assert.Equal(12, result.Fulfilled);
        Assert.Equal(WeekFollowThroughBuilder.MaxItems, result.FulfilledItems.Count);
    }

    // ---- service ----

    private sealed class Store : IOwnerSettingsStore
    {
        public readonly Dictionary<string, object> Values = [];
        public Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken cancellationToken) where T : class =>
            Task.FromResult(Values.TryGetValue(section, out var value) ? value as T : null);
        public Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken cancellationToken) where T : class
        {
            Values[section] = value;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private static WeekFollowThroughService Service(List<(Guid Id, DateOnly? On)> updates, Commitment? current = null,
        ProjectRecord? project = null, List<ProjectFocusInput>? focused = null, Store? store = null) =>
        new(Fake<ICommitmentService>.Create(
                ("TodayAsync", _ => Today),
                ("ListAsync", _ => (IReadOnlyList<Commitment>)(current is null ? [] : [current])),
                ("GetAsync", args => current is { } found && found.Id == (Guid)args[0]! ? found : null),
                ("UpdateAsync", args =>
                {
                    updates.Add(((Guid)args[0]!, (DateOnly?)args[3]));
                    return InboxOperation<Commitment>.Ok(current!);
                })),
            Fake<IDecisionService>.Create(("ListAsync", _ => (IReadOnlyList<DecisionView>)[])),
            Fake<IProjectStore>.Create(
                ("ListAsync", _ => (IReadOnlyList<ProjectRecord>)(project is null ? [] : [project])),
                ("GetAsync", args => project is { } p && p.Id == (Guid)args[0]! ? p : null)),
            Fake<IProjectFocusService>.Create(
                ("DescribeAsync", _ => (ProjectNextActionView?)null),
                ("SetAsync", args =>
                {
                    focused?.Add((ProjectFocusInput)args[2]!);
                    return project;
                })),
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => (DailyBriefingPreferenceRecord?)null)),
            store ?? new Store(), new FixedClock());

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task Reading_the_review_section_changes_nothing()
    {
        var updates = new List<(Guid, DateOnly?)>();
        var focused = new List<ProjectFocusInput>();
        var service = Service(updates, Promise("Old", due: Today.AddDays(-9)), Proj("Kitchen"), focused);

        var result = await service.GetAsync(Owner, null, default);

        Assert.Equal(WeekStart, result.WeekStart);
        Assert.NotEmpty(result.Suggestions);
        Assert.Empty(updates);
        Assert.Empty(focused);
    }

    private static ProjectRecord Proj(string name) =>
        new(ProjectId, name, null, null, "blue", Created, Created, 0, 0, 0, "Outcome", "active", null);

    private static readonly Guid ProjectId = Guid.CreateVersion7();

    [Fact]
    public async Task Accepting_a_plan_moves_one_promise_to_the_chosen_day()
    {
        var updates = new List<(Guid Id, DateOnly? On)>();
        var overdue = Promise("Old", due: Today.AddDays(-9));
        var service = Service(updates, overdue);

        var accepted = await service.AcceptAsync(Owner, new FollowThroughAcceptance(FollowThroughKinds.PlanCommitment,
            overdue.Id.ToString("D"), new DateOnly(2026, 10, 21)), default);

        Assert.True(accepted.Succeeded);
        Assert.Equal($"commitment:{overdue.Id:D}", accepted.Value!.Ref);
        var update = Assert.Single(updates);
        Assert.Equal((overdue.Id, (DateOnly?)new DateOnly(2026, 10, 21)), update);
    }

    [Fact]
    public async Task A_plan_needs_a_day_that_is_not_in_the_past_and_a_promise_that_is_still_open()
    {
        var updates = new List<(Guid Id, DateOnly? On)>();
        var overdue = Promise("Old", due: Today.AddDays(-9));
        var service = Service(updates, overdue);
        var id = overdue.Id.ToString("D");

        Assert.Equal("on", (await service.AcceptAsync(Owner, new FollowThroughAcceptance(FollowThroughKinds.PlanCommitment, id), default)).Field);
        Assert.Equal("on", (await service.AcceptAsync(Owner, new FollowThroughAcceptance(FollowThroughKinds.PlanCommitment, id, Today.AddDays(-1)), default)).Field);
        Assert.Equal(FollowThroughFailure.NotFound, (await service.AcceptAsync(Owner,
            new FollowThroughAcceptance(FollowThroughKinds.PlanCommitment, Guid.CreateVersion7().ToString("D"), Today), default)).Failure);
        Assert.Equal(FollowThroughFailure.NotFound, (await Service(updates, Promise("Done", CommitmentStatuses.Done)).AcceptAsync(Owner,
            new FollowThroughAcceptance(FollowThroughKinds.PlanCommitment, id, Today), default)).Failure);
        Assert.Equal("kind", (await service.AcceptAsync(Owner, new FollowThroughAcceptance("bogus", id), default)).Field);
        Assert.Equal("targetId", (await service.AcceptAsync(Owner, new FollowThroughAcceptance(FollowThroughKinds.PlanCommitment, "x"), default)).Field);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task Accepting_a_project_step_keeps_the_outcome_and_needs_a_note_or_a_link()
    {
        var focused = new List<ProjectFocusInput>();
        var service = Service([], project: Proj("Kitchen"), focused: focused);
        var id = ProjectId.ToString("D");

        Assert.Equal("note", (await service.AcceptAsync(Owner, new FollowThroughAcceptance(FollowThroughKinds.ProjectNextStep, id), default)).Field);
        Assert.Empty(focused);

        var accepted = await service.AcceptAsync(Owner, new FollowThroughAcceptance(FollowThroughKinds.ProjectNextStep, id, Note: "Call the plumber"), default);

        Assert.True(accepted.Succeeded);
        var input = Assert.Single(focused);
        Assert.Equal("Outcome", input.Outcome);
        Assert.Equal("Call the plumber", input.NextAction!.Note);
        Assert.Equal(FollowThroughFailure.NotFound, (await service.AcceptAsync(Owner,
            new FollowThroughAcceptance(FollowThroughKinds.ProjectNextStep, Guid.CreateVersion7().ToString("D"), Note: "x"), default)).Failure);
    }

    [Fact]
    public async Task Dismissing_hides_that_suggestion_for_weeks_and_rejects_nonsense()
    {
        var store = new Store();
        var overdue = Promise("Old", due: Today.AddDays(-9));
        var service = Service([], overdue, store: store);
        var id = overdue.Id.ToString("D");
        Assert.Single((await service.GetAsync(Owner, null, default)).Suggestions);

        Assert.True(await service.DismissAsync(Owner, FollowThroughKinds.PlanCommitment, id, default));

        Assert.Empty((await service.GetAsync(Owner, null, default)).Suggestions);
        Assert.False(await service.DismissAsync(Owner, "bogus", id, default));
        Assert.False(await service.DismissAsync(Owner, FollowThroughKinds.PlanCommitment, "not-a-guid", default));
        var saved = (FollowThroughDismissals)store.Values[FollowThroughDismissals.Section];
        Assert.True(saved.Active(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)).Count == 1);
        Assert.Empty(saved.Active(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
