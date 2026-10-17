using Jarvis.Application.Habits;
using Jarvis.Application.Inbox;
using Jarvis.Application.Planner;
using Jarvis.Application.Projects;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Goals;

/// <summary>
/// Goals and their weekly feedback loop, kept in owner settings. A goal links existing habits, a project and
/// promises instead of copying them; plans and suggestions are proposals the owner accepts, reduces or declines.
/// </summary>
public sealed class GoalService(
    IOwnerSettingsStore settings,
    IHabitService habits,
    IProjectStore projects,
    IProjectFocusService projectFocus,
    ICommitmentService commitments,
    IDayPlannerService planner,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IGoalService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<GoalSummary>> ListAsync(Guid ownerId, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var (today, zone) = await TodayAsync(ownerId, cancellationToken);
        return (await LoadAsync(ownerId, cancellationToken))
            .Where(goal => includeArchived || goal.Status != GoalStatuses.Archived)
            .OrderBy(goal => goal.Status == GoalStatuses.Active ? 0 : 1)
            .ThenBy(goal => goal.CreatedAt)
            .Select(goal =>
            {
                var season = GoalLogic.CurrentSeason(goal);
                return new GoalSummary(goal.Id, goal.Title, goal.Status, GoalLogic.CheckInDue(goal, today, zone),
                    season.Count, season.LastOrDefault()?.At, season.LastOrDefault()?.Feeling,
                    GoalLogic.Review(goal, today),
                    goal.Plan?.Items.Count(item => item.Status is GoalPlanStatuses.Proposed or GoalPlanStatuses.Accepted) ?? 0);
            }).ToArray();
    }

    public async Task<GoalView?> GetAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken)
    {
        var goal = (await LoadAsync(ownerId, cancellationToken)).FirstOrDefault(item => item.Id == goalId);
        return goal is null ? null : await ViewAsync(ownerId, goal, cancellationToken);
    }

    public async Task<GoalOperation<GoalView>> CreateAsync(Guid ownerId, GoalInput input,
        CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        if (all.Count >= GoalRules.MaxGoals)
            return GoalOperation<GoalView>.Conflict("goals", $"Keep at most {GoalRules.MaxGoals} goals. Archive one first.");
        var now = clock.GetUtcNow();
        var seed = new GoalRecord(Guid.NewGuid(), string.Empty, null, null, null, GoalRules.DefaultCheckInDays,
            GoalRules.DefaultWeeklyMinutes, GoalStatuses.Active, null, [], [], now, now, [], null, [],
            new Dictionary<string, DateOnly>());
        var applied = await ApplyAsync(ownerId, seed, input, create: true, cancellationToken);
        if (!applied.Succeeded) return GoalOperation<GoalView>.Invalid(applied.Field!, applied.Message!);
        await SaveAsync(ownerId, [.. all, applied.Value!], cancellationToken);
        return GoalOperation<GoalView>.Ok(await ViewAsync(ownerId, applied.Value!, cancellationToken));
    }

    public Task<GoalOperation<GoalView>> UpdateAsync(Guid ownerId, Guid goalId, GoalInput input,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, _) => ApplyAsync(ownerId, goal, input, create: false, cancellationToken),
            cancellationToken);

    public async Task<bool> DeleteAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        if (all.All(goal => goal.Id != goalId)) return false;
        await SaveAsync(ownerId, all.Where(goal => goal.Id != goalId).ToArray(), cancellationToken);
        return true;
    }

    public Task<GoalOperation<GoalView>> ResetSeasonAsync(Guid ownerId, Guid goalId,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, _) => Task.FromResult(GoalOperation<GoalRecord>.Ok(goal with
        {
            Season = goal.Season + 1,
            Plan = null,
            Declined = new Dictionary<string, DateOnly>(),
            ReviewDismissedOn = null,
            Status = goal.Status == GoalStatuses.Archived ? GoalStatuses.Archived : GoalStatuses.Active
        })), cancellationToken);

    public Task<GoalOperation<GoalView>> AddCheckInAsync(Guid ownerId, Guid goalId, CheckInInput input,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, _) =>
        {
            var progress = GoalLogic.Clean(input.Progress, GoalRules.MaxText);
            if (progress.Length == 0)
                return Task.FromResult(GoalOperation<GoalRecord>.Invalid("progress", "Say what moved, even if it is small."));
            var feeling = input.Feeling ?? GoalFeelings.OnTrack;
            if (!GoalFeelings.All.Contains(feeling))
                return Task.FromResult(GoalOperation<GoalRecord>.Invalid("feeling", "Choose on track, slipping or stuck."));
            if (goal.CheckIns.Count >= GoalRules.MaxCheckIns)
                return Task.FromResult(GoalOperation<GoalRecord>.Conflict("checkIns", "This goal has all the check-ins it can hold."));
            var friction = GoalLogic.Clean(input.Friction, GoalRules.MaxText);
            var adjustment = GoalLogic.Clean(input.Adjustment, GoalRules.MaxText);
            var checkIn = new GoalCheckIn(Guid.NewGuid(), clock.GetUtcNow(), progress,
                friction.Length == 0 ? null : friction, adjustment.Length == 0 ? null : adjustment, feeling,
                goal.Season);
            return Task.FromResult(GoalOperation<GoalRecord>.Ok(goal with { CheckIns = [.. goal.CheckIns, checkIn] }));
        }, cancellationToken);

    public Task<GoalOperation<GoalView>> ProposePlanAsync(Guid ownerId, Guid goalId,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, async (goal, today) =>
        {
            if (goal.Status != GoalStatuses.Active)
                return GoalOperation<GoalRecord>.Conflict("status", "Resume the goal before planning its week.");
            var week = GoalLogic.MondayOf(today);
            var kept = goal.Plan is { } plan && plan.WeekStart == week
                ? plan.Items.Where(item => item.Status is GoalPlanStatuses.Accepted or GoalPlanStatuses.Done
                    or GoalPlanStatuses.Declined)
                : [];
            var candidates = await CandidatesAsync(ownerId, goal, today, cancellationToken);
            return GoalOperation<GoalRecord>.Ok(goal with { Plan = GoalLogic.Propose(goal, today, candidates, kept) });
        }, cancellationToken);

    public Task<GoalOperation<GoalView>> ActOnPlanItemAsync(Guid ownerId, Guid goalId, Guid itemId,
        PlanItemAction action, string? timeZoneId, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, async (goal, today) =>
        {
            var item = goal.Plan?.Items.FirstOrDefault(entry => entry.Id == itemId);
            if (item is null) return GoalOperation<GoalRecord>.NotFound();
            GoalPlanItem? changed = null;
            var declined = goal.Declined;
            switch (action.Action)
            {
                case "accept" when item.Status == GoalPlanStatuses.Proposed:
                    changed = item with { Status = GoalPlanStatuses.Accepted };
                    break;
                case "reduce" when item.Status is GoalPlanStatuses.Proposed or GoalPlanStatuses.Accepted:
                    if (action.Minutes is not { } minutes || minutes < 5 || minutes >= item.Minutes)
                        return GoalOperation<GoalRecord>.Invalid("minutes",
                            $"Pick between 5 and {item.Minutes - 1} minutes.");
                    changed = item with { Minutes = minutes, Status = GoalPlanStatuses.Accepted };
                    break;
                case "decline" when item.Status is GoalPlanStatuses.Proposed or GoalPlanStatuses.Accepted:
                    changed = item with { Status = GoalPlanStatuses.Declined };
                    declined = new Dictionary<string, DateOnly>(goal.Declined) { [item.Key] = today };
                    break;
                case "done" when item.Status is GoalPlanStatuses.Proposed or GoalPlanStatuses.Accepted:
                    changed = item with { Status = GoalPlanStatuses.Done };
                    break;
                case "schedule" when item.Status == GoalPlanStatuses.Accepted:
                    try
                    {
                        await planner.AddItemAsync(ownerId, new AddDayPlanItemRequest(item.Title, item.Minutes,
                            item.Ref is { } r && r.StartsWith("commitment:", StringComparison.Ordinal) ? r : null),
                            timeZoneId, cancellationToken);
                    }
                    catch (ArgumentException exception)
                    {
                        return GoalOperation<GoalRecord>.Invalid("schedule", exception.Message);
                    }
                    return GoalOperation<GoalRecord>.Ok(goal);
                case "accept" or "reduce" or "decline" or "done" or "schedule":
                    return GoalOperation<GoalRecord>.Conflict("action",
                        item.Status == GoalPlanStatuses.Proposed && action.Action == "schedule"
                            ? "Accept the item before scheduling it."
                            : $"That item is already {item.Status}.");
                default:
                    return GoalOperation<GoalRecord>.Invalid("action",
                        "Choose accept, reduce, decline, done or schedule.");
            }
            return GoalOperation<GoalRecord>.Ok(goal with
            {
                Declined = declined,
                Plan = goal.Plan! with
                {
                    Items = goal.Plan.Items.Select(entry => entry.Id == itemId ? changed! : entry).ToArray()
                }
            });
        }, cancellationToken);

    public Task<GoalOperation<GoalView>> StartExperimentAsync(Guid ownerId, Guid goalId, ExperimentInput input,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, today) =>
        {
            var change = GoalLogic.Clean(input.Change, GoalRules.MaxText);
            var benefit = GoalLogic.Clean(input.ExpectedBenefit, GoalRules.MaxText);
            if (change.Length == 0)
                return Task.FromResult(GoalOperation<GoalRecord>.Invalid("change", "Name the one thing you will change."));
            if (benefit.Length == 0)
                return Task.FromResult(GoalOperation<GoalRecord>.Invalid("expectedBenefit", "Say what you hope it will do."));
            if (input.DurationDays is not (>= GoalRules.MinExperimentDays and <= GoalRules.MaxExperimentDays))
                return Task.FromResult(GoalOperation<GoalRecord>.Invalid("durationDays",
                    $"Run it for {GoalRules.MinExperimentDays} to {GoalRules.MaxExperimentDays} days."));
            if (goal.Experiments.Any(item => item.Status == ExperimentStatuses.Running))
                return Task.FromResult(GoalOperation<GoalRecord>.Conflict("experiment",
                    "One experiment at a time. Finish the running one first."));
            if (goal.Experiments.Count >= GoalRules.MaxExperiments)
                return Task.FromResult(GoalOperation<GoalRecord>.Conflict("experiment", "This goal has all the experiments it can hold."));
            var experiment = new GoalExperiment(Guid.NewGuid(), change, benefit, input.DurationDays.Value, today,
                today.AddDays(input.DurationDays.Value), [], ExperimentStatuses.Running, null);
            return Task.FromResult(GoalOperation<GoalRecord>.Ok(goal with { Experiments = [.. goal.Experiments, experiment] }));
        }, cancellationToken);

    public Task<GoalOperation<GoalView>> NoteExperimentAsync(Guid ownerId, Guid goalId, Guid experimentId,
        string? text, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, today) =>
        {
            var experiment = goal.Experiments.FirstOrDefault(item => item.Id == experimentId);
            if (experiment is null) return Task.FromResult(GoalOperation<GoalRecord>.NotFound());
            var note = GoalLogic.Clean(text, GoalRules.MaxText);
            if (note.Length == 0) return Task.FromResult(GoalOperation<GoalRecord>.Invalid("text", "Write what you noticed."));
            if (experiment.Status != ExperimentStatuses.Running)
                return Task.FromResult(GoalOperation<GoalRecord>.Conflict("experiment", "That experiment is finished."));
            if (experiment.Notes.Count >= GoalRules.MaxNotes)
                return Task.FromResult(GoalOperation<GoalRecord>.Conflict("experiment", "That experiment has all the notes it can hold."));
            return Task.FromResult(GoalOperation<GoalRecord>.Ok(Replace(goal, experiment with
            {
                Notes = [.. experiment.Notes, new ExperimentNote(today, note)]
            })));
        }, cancellationToken);

    public Task<GoalOperation<GoalView>> FinishExperimentAsync(Guid ownerId, Guid goalId, Guid experimentId,
        bool keep, string? conclusion, CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, _) =>
        {
            var experiment = goal.Experiments.FirstOrDefault(item => item.Id == experimentId);
            if (experiment is null) return Task.FromResult(GoalOperation<GoalRecord>.NotFound());
            if (experiment.Status != ExperimentStatuses.Running)
                return Task.FromResult(GoalOperation<GoalRecord>.Conflict("experiment", "That experiment is already finished."));
            var text = GoalLogic.Clean(conclusion, GoalRules.MaxText);
            return Task.FromResult(GoalOperation<GoalRecord>.Ok(Replace(goal, experiment with
            {
                Status = keep ? ExperimentStatuses.Kept : ExperimentStatuses.Stopped,
                Conclusion = text.Length == 0 ? null : text
            })));
        }, cancellationToken);

    public Task<GoalOperation<GoalView>> DismissReviewAsync(Guid ownerId, Guid goalId,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, goalId, (goal, today) =>
            Task.FromResult(GoalOperation<GoalRecord>.Ok(goal with { ReviewDismissedOn = today })), cancellationToken);

    private static GoalRecord Replace(GoalRecord goal, GoalExperiment experiment) =>
        goal with { Experiments = goal.Experiments.Select(item => item.Id == experiment.Id ? experiment : item).ToArray() };

    private async Task<GoalOperation<GoalView>> MutateAsync(Guid ownerId, Guid goalId,
        Func<GoalRecord, DateOnly, Task<GoalOperation<GoalRecord>>> change, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        var goal = all.FirstOrDefault(item => item.Id == goalId);
        if (goal is null) return GoalOperation<GoalView>.NotFound();
        var (today, _) = await TodayAsync(ownerId, cancellationToken);
        var result = await change(goal, today);
        if (!result.Succeeded) return new GoalOperation<GoalView>(default, result.Failure, result.Field, result.Message);
        var updated = result.Value! with { UpdatedAt = clock.GetUtcNow() };
        await SaveAsync(ownerId, all.Select(item => item.Id == goalId ? updated : item).ToArray(), cancellationToken);
        return GoalOperation<GoalView>.Ok(await ViewAsync(ownerId, updated, cancellationToken));
    }

    private async Task<GoalOperation<GoalRecord>> ApplyAsync(Guid ownerId, GoalRecord goal, GoalInput input,
        bool create, CancellationToken cancellationToken)
    {
        var title = input.Title is null ? goal.Title : GoalLogic.Clean(input.Title, GoalRules.MaxTitle + 1);
        if (title.Length == 0) return GoalOperation<GoalRecord>.Invalid("title", "Say what you want to achieve.");
        if (title.Length > GoalRules.MaxTitle)
            return GoalOperation<GoalRecord>.Invalid("title", $"Keep the goal under {GoalRules.MaxTitle} characters.");
        var every = input.CheckInEveryDays ?? goal.CheckInEveryDays;
        if (every is < GoalRules.MinCheckInDays or > GoalRules.MaxCheckInDays)
            return GoalOperation<GoalRecord>.Invalid("checkInEveryDays",
                $"Check in every {GoalRules.MinCheckInDays} to {GoalRules.MaxCheckInDays} days.");
        var minutes = input.WeeklyMinutes ?? goal.WeeklyMinutes;
        if (minutes is < GoalRules.MinWeeklyMinutes or > GoalRules.MaxWeeklyMinutes)
            return GoalOperation<GoalRecord>.Invalid("weeklyMinutes",
                $"Set aside {GoalRules.MinWeeklyMinutes} to {GoalRules.MaxWeeklyMinutes} minutes a week.");
        var status = input.Status ?? goal.Status;
        if (!GoalStatuses.All.Contains(status))
            return GoalOperation<GoalRecord>.Invalid("status", "Choose active, paused, achieved or archived.");
        var motivation = input.Motivation is null ? goal.Motivation : GoalLogic.Clean(input.Motivation, GoalRules.MaxText);
        var target = input.Target is null ? goal.Target : GoalLogic.Clean(input.Target, GoalRules.MaxText);

        var projectId = input.ClearProject ? null : input.ProjectId ?? goal.ProjectId;
        if (projectId is { } project && project != goal.ProjectId &&
            await projects.GetAsync(project, ownerId, cancellationToken) is null)
            return GoalOperation<GoalRecord>.Invalid("projectId", "That project was not found.");

        var habitIds = input.HabitIds?.Distinct().ToArray() ?? goal.HabitIds.ToArray();
        if (habitIds.Length > GoalRules.MaxHabits)
            return GoalOperation<GoalRecord>.Invalid("habitIds", $"Link at most {GoalRules.MaxHabits} habits.");
        foreach (var id in habitIds.Except(goal.HabitIds))
            if (await habits.GetAsync(id, ownerId, cancellationToken) is null)
                return GoalOperation<GoalRecord>.Invalid("habitIds", "A linked habit was not found.");

        var commitmentIds = input.CommitmentIds?.Distinct().ToArray() ?? goal.CommitmentIds.ToArray();
        if (commitmentIds.Length > GoalRules.MaxCommitments)
            return GoalOperation<GoalRecord>.Invalid("commitmentIds", $"Link at most {GoalRules.MaxCommitments} promises.");
        foreach (var id in commitmentIds.Except(goal.CommitmentIds))
            if (await commitments.GetAsync(id, ownerId, cancellationToken) is null)
                return GoalOperation<GoalRecord>.Invalid("commitmentIds", "A linked promise was not found.");

        var (today, _) = await TodayAsync(ownerId, cancellationToken);
        var horizon = input.ClearHorizon ? null : input.Horizon ?? goal.Horizon;
        if (create && horizon is { } day && day < today)
            return GoalOperation<GoalRecord>.Invalid("horizon", "Pick a horizon that has not passed.");

        return GoalOperation<GoalRecord>.Ok(goal with
        {
            Title = title,
            Motivation = string.IsNullOrEmpty(motivation) ? null : motivation,
            Target = string.IsNullOrEmpty(target) ? null : target,
            Horizon = horizon,
            CheckInEveryDays = every,
            WeeklyMinutes = minutes,
            Status = status,
            ProjectId = projectId,
            HabitIds = habitIds,
            CommitmentIds = commitmentIds,
            // A changed horizon or status is the owner's own answer to a "stop" suggestion.
            ReviewDismissedOn = horizon != goal.Horizon || status != goal.Status ? null : goal.ReviewDismissedOn
        });
    }

    private async Task<IReadOnlyList<PlanCandidate>> CandidatesAsync(Guid ownerId, GoalRecord goal, DateOnly today,
        CancellationToken cancellationToken)
    {
        var list = new List<PlanCandidate>();
        if (goal.ProjectId is { } projectId &&
            await projects.GetAsync(projectId, ownerId, cancellationToken) is { } project &&
            await projectFocus.DescribeAsync(ownerId, project, cancellationToken) is
                { State: ProjectNextActionStates.Open } next &&
            !string.IsNullOrWhiteSpace(next.Title ?? next.Note))
            list.Add(new PlanCandidate($"project:{projectId}", GoalLogic.Clean(next.Title ?? next.Note, 120),
                GoalRules.ProposalMinutes, $"project:{projectId}"));

        var weekEnd = GoalLogic.MondayOf(today).AddDays(6);
        foreach (var id in goal.CommitmentIds)
        {
            var promise = await commitments.GetAsync(id, ownerId, cancellationToken);
            if (promise is { Suggested: false, Status: CommitmentStatuses.Open } &&
                (promise.DueOn is null || promise.DueOn <= weekEnd))
                list.Add(new PlanCandidate($"commitment:{id}", GoalLogic.Clean(promise.Description, 120),
                    GoalRules.ProposalMinutes, $"commitment:{id}"));
        }

        foreach (var id in goal.HabitIds)
        {
            var summary = await habits.GetAsync(id, ownerId, cancellationToken);
            if (summary is null || summary.Habit.IsArchived) continue;
            var remaining = Math.Max(0, summary.Habit.TargetPerWeek - summary.Stats.ThisWeekCount);
            if (remaining == 0) continue;
            list.Add(new PlanCandidate($"habit:{id}", $"{summary.Habit.Name} ×{remaining} this week",
                remaining * GoalRules.HabitSessionMinutes, $"habit:{id}"));
        }
        return list;
    }

    private async Task<GoalView> ViewAsync(Guid ownerId, GoalRecord goal, CancellationToken cancellationToken)
    {
        var (today, zone) = await TodayAsync(ownerId, cancellationToken);
        var habitViews = new List<GoalHabitView>();
        foreach (var id in goal.HabitIds)
            if (await habits.GetAsync(id, ownerId, cancellationToken) is { } summary)
                habitViews.Add(new GoalHabitView(id, summary.Habit.Name, summary.Stats.ThisWeekCount,
                    summary.Habit.TargetPerWeek, summary.Stats.CurrentStreak, summary.Habit.IsArchived));

        GoalProjectView? projectView = null;
        if (goal.ProjectId is { } projectId && await projects.GetAsync(projectId, ownerId, cancellationToken) is { } p)
        {
            var next = await projectFocus.DescribeAsync(ownerId, p, cancellationToken);
            projectView = new GoalProjectView(p.Id, p.Name, p.Outcome, next?.Title ?? next?.Note);
        }

        var promiseViews = new List<GoalCommitmentView>();
        foreach (var id in goal.CommitmentIds)
            if (await commitments.GetAsync(id, ownerId, cancellationToken) is { } c)
                promiseViews.Add(new GoalCommitmentView(c.Id, c.Description, c.Counterparty, c.Status, c.DueOn));

        var done = goal.Plan?.Items.Count(item => item.Status == GoalPlanStatuses.Done) ?? 0;
        var measured = habitViews.Select(h => $"{h.Name}: {h.ThisWeekCount} of {h.TargetPerWeek} this week").ToList();
        if (goal.Plan is not null) measured.Add($"{done} planned step{(done == 1 ? "" : "s")} done this week");
        measured.Add($"{promiseViews.Count(x => x.Status == CommitmentStatuses.Done)} of {promiseViews.Count} linked promises kept");

        var reflections = GoalLogic.CurrentSeason(goal).TakeLast(5).Reverse().ToArray();
        return new GoalView(goal, GoalLogic.CheckInDue(goal, today, zone), GoalLogic.DaysSinceCheckIn(goal, today, zone),
            habitViews, projectView, promiseViews,
            new GoalProgressView(measured, reflections, GoalLogic.Review(goal, today)));
    }

    private async Task<IReadOnlyList<GoalRecord>> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await settings.GetAsync<GoalState>(ownerId, GoalState.Section, cancellationToken))?.Goals ?? [];

    private Task SaveAsync(Guid ownerId, IReadOnlyList<GoalRecord> goals, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, GoalState.Section, new GoalState(goals), cancellationToken);

    private async Task<(DateOnly Today, TimeZoneInfo Zone)> TodayAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime), zone);
    }
}
