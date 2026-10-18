using System.Globalization;
using Jarvis.Application.Decisions;
using Jarvis.Application.Events;
using Jarvis.Application.Inbox;
using Jarvis.Application.Projects;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Reviews;

public static class FollowThroughKinds
{
    /// <summary>Give an overdue promise a new day.</summary>
    public const string PlanCommitment = "plan_commitment";

    /// <summary>Choose the next step for a project that has none, or whose step is done.</summary>
    public const string ProjectNextStep = "project_next_step";
}

public sealed record FollowThroughItem(string Ref, string Title, string? Detail);

/// <summary>
/// One optional next step for the coming week. It is only a proposal: nothing exists until the owner accepts it, and each
/// acceptance changes exactly one existing record. <see cref="SuggestedOn"/> is a starting point the owner can change.
/// </summary>
public sealed record FollowThroughSuggestion(string Kind, string TargetId, string Title, string Reason,
    DateOnly? SuggestedOn);

/// <summary>
/// What the week left behind, counted from the owner's actual records: promises fulfilled, dropped and still overdue,
/// decisions to review next week, and projects that need a next step.
/// </summary>
public sealed record WeekFollowThrough(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int Fulfilled,
    int Dropped,
    int Overdue,
    int Stale,
    int DecisionsDueNextWeek,
    IReadOnlyList<FollowThroughItem> FulfilledItems,
    IReadOnlyList<FollowThroughItem> StaleItems,
    IReadOnlyList<FollowThroughItem> NextWeekDecisions,
    IReadOnlyList<FollowThroughItem> ProjectsNeedingStep,
    IReadOnlyList<FollowThroughSuggestion> Suggestions);

public sealed record WeekProject(Guid Id, string Name, string Status, ProjectNextActionView? NextAction);

public sealed record FollowThroughInputs(
    DateOnly WeekStart,
    DateOnly Today,
    TimeZoneInfo Zone,
    IReadOnlyList<Commitment> Commitments,
    IReadOnlyList<DecisionView> Decisions,
    IReadOnlyList<WeekProject> Projects,
    IReadOnlySet<string> Dismissed);

/// <summary>Pure rules for the follow-through section of the weekly review.</summary>
public static class WeekFollowThroughBuilder
{
    public const int MaxItems = 5;
    public const int MaxSuggestions = 3;
    public const int StaleAfterDays = 7;

    public static string Fingerprint(string kind, string targetId) => $"{kind}:{targetId}";

    public static WeekFollowThrough Build(FollowThroughInputs input)
    {
        var weekEnd = input.WeekStart.AddDays(6);
        DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, input.Zone).DateTime);
        bool InWeek(DateTimeOffset? at) => at is { } value && Local(value) >= input.WeekStart && Local(value) <= weekEnd;

        var promises = input.Commitments.Where(item => !item.Suggested).ToArray();
        var fulfilled = promises.Where(item => item.Status == CommitmentStatuses.Done && InWeek(item.CompletedAt))
            .OrderBy(item => item.CompletedAt).ThenBy(item => item.Id).ToArray();
        var dropped = promises.Count(item => item.Status == CommitmentStatuses.Dropped && InWeek(item.CompletedAt));
        var overdue = promises
            .Where(item => item.Status == CommitmentStatuses.Open && item.DueOn is { } due && due < input.Today)
            .OrderBy(item => item.DueOn).ThenBy(item => item.Id).ToArray();
        var stale = overdue.Where(item => input.Today.DayNumber - item.DueOn!.Value.DayNumber >= StaleAfterDays).ToArray();

        var nextWeekStart = weekEnd.AddDays(1);
        var nextWeekEnd = weekEnd.AddDays(7);
        var decisions = input.Decisions
            .Where(item => item.Status != DecisionStatuses.Resolved && item.ReviewOn >= nextWeekStart && item.ReviewOn <= nextWeekEnd)
            .OrderBy(item => item.ReviewOn).ThenBy(item => item.Id).ToArray();

        var needStep = input.Projects
            .Where(item => item.Status == "active")
            .Where(item => item.NextAction is null || item.NextAction.State is ProjectNextActionStates.Done or ProjectNextActionStates.Gone)
            .OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Id).ToArray();

        var suggestions = new List<FollowThroughSuggestion>();
        void Add(FollowThroughSuggestion suggestion)
        {
            if (suggestions.Count >= MaxSuggestions) return;
            if (input.Dismissed.Contains(Fingerprint(suggestion.Kind, suggestion.TargetId))) return;
            suggestions.Add(suggestion);
        }

        var mine = overdue.Where(item => item.Direction == CommitmentDirections.IOwe).ToArray();
        var startOfNextWeek = nextWeekStart < input.Today ? input.Today : nextWeekStart;
        if (mine.Length > 0) Add(PlanFor(mine[0], input.Today, startOfNextWeek));
        if (needStep.Length > 0)
            Add(new FollowThroughSuggestion(FollowThroughKinds.ProjectNextStep, needStep[0].Id.ToString("D"),
                $"Choose the next step for “{needStep[0].Name}”",
                needStep[0].NextAction is null ? "It has no next step yet." : "Its last next step is done.", null));
        if (mine.Length > 1) Add(PlanFor(mine[1], input.Today, startOfNextWeek));

        return new WeekFollowThrough(input.WeekStart, weekEnd, fulfilled.Length, dropped, overdue.Length, stale.Length,
            decisions.Length,
            fulfilled.Take(MaxItems).Select(item => new FollowThroughItem(new EntityRef(EntityTypes.Commitment, item.Id).ToString(),
                item.Description, item.Counterparty)).ToArray(),
            stale.Take(MaxItems).Select(item => new FollowThroughItem(new EntityRef(EntityTypes.Commitment, item.Id).ToString(),
                item.Description, $"{input.Today.DayNumber - item.DueOn!.Value.DayNumber} days overdue")).ToArray(),
            decisions.Take(MaxItems).Select(item => new FollowThroughItem(new EntityRef(EntityTypes.Decision, item.Id).ToString(),
                item.Title, item.ReviewOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToArray(),
            needStep.Take(MaxItems).Select(item => new FollowThroughItem(new EntityRef(EntityTypes.Project, item.Id).ToString(),
                item.Name, item.NextAction is null ? "No next step" : "Last step done")).ToArray(),
            suggestions);
    }

    private static FollowThroughSuggestion PlanFor(Commitment item, DateOnly today, DateOnly on) =>
        new(FollowThroughKinds.PlanCommitment, item.Id.ToString("D"), $"Give “{item.Description}” a new day",
            $"It has been overdue for {today.DayNumber - item.DueOn!.Value.DayNumber} days.", on);
}

public sealed record FollowThroughAcceptance(string Kind, string TargetId, DateOnly? On = null, string? Note = null,
    string? LinkType = null, Guid? LinkId = null);

public sealed record FollowThroughResult(string Ref);

public enum FollowThroughFailure
{
    None,
    NotFound,
    Invalid
}

public sealed record FollowThroughOperation<T>(T? Value, FollowThroughFailure Failure = FollowThroughFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == FollowThroughFailure.None;
    public static FollowThroughOperation<T> Ok(T value) => new(value);
    public static FollowThroughOperation<T> NotFound() => new(default, FollowThroughFailure.NotFound);
    public static FollowThroughOperation<T> Invalid(string field, string message) =>
        new(default, FollowThroughFailure.Invalid, field, message);
}

/// <summary>The suggestions an owner put away, kept for a few weeks so the same one does not come straight back.</summary>
public sealed record FollowThroughDismissals(IReadOnlyDictionary<string, DateTimeOffset>? Until = null)
{
    public const string Section = "weekly-review.followthrough";
    public static readonly TimeSpan HideFor = TimeSpan.FromDays(28);

    public IReadOnlySet<string> Active(DateTimeOffset now) =>
        (Until ?? new Dictionary<string, DateTimeOffset>()).Where(pair => pair.Value > now).Select(pair => pair.Key)
        .ToHashSet(StringComparer.Ordinal);

    public FollowThroughDismissals With(string fingerprint, DateTimeOffset now)
    {
        var until = (Until ?? new Dictionary<string, DateTimeOffset>()).Where(pair => pair.Value > now)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        until[fingerprint] = now + HideFor;
        return new FollowThroughDismissals(until);
    }
}

public interface IWeekFollowThroughService
{
    /// <summary>Read-only: building the section creates nothing.</summary>
    Task<WeekFollowThrough> GetAsync(Guid ownerId, DateOnly? weekStart, CancellationToken cancellationToken);

    Task<FollowThroughOperation<FollowThroughResult>> AcceptAsync(Guid ownerId, FollowThroughAcceptance request,
        CancellationToken cancellationToken);

    Task<bool> DismissAsync(Guid ownerId, string kind, string targetId, CancellationToken cancellationToken);
}

public sealed class WeekFollowThroughService(
    ICommitmentService commitments,
    IDecisionService decisions,
    IProjectStore projects,
    IProjectFocusService focus,
    IDailyBriefingRepository briefings,
    IOwnerSettingsStore settings,
    TimeProvider? timeProvider = null) : IWeekFollowThroughService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<WeekFollowThrough> GetAsync(Guid ownerId, DateOnly? weekStart, CancellationToken cancellationToken)
    {
        var zone = await ZoneAsync(ownerId, cancellationToken);
        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        var start = WeeklyReviewClock.WeekStartOf(weekStart ?? today);
        var projectList = new List<WeekProject>();
        foreach (var project in await projects.ListAsync(ownerId, cancellationToken))
            projectList.Add(new WeekProject(project.Id, project.Name, project.Status,
                await focus.DescribeAsync(ownerId, project, cancellationToken)));
        var dismissed = (await settings.GetAsync<FollowThroughDismissals>(ownerId, FollowThroughDismissals.Section,
            cancellationToken))?.Active(clock.GetUtcNow()) ?? new HashSet<string>();
        return WeekFollowThroughBuilder.Build(new FollowThroughInputs(start, today, zone,
            await commitments.ListAsync(ownerId, null, null, false, cancellationToken),
            await decisions.ListAsync(ownerId, null, DecisionRules.MaxLimit, cancellationToken), projectList, dismissed));
    }

    public async Task<FollowThroughOperation<FollowThroughResult>> AcceptAsync(Guid ownerId,
        FollowThroughAcceptance request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.TargetId, out var targetId))
            return FollowThroughOperation<FollowThroughResult>.Invalid("targetId", "Choose something from the list.");
        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        switch (request.Kind)
        {
            case FollowThroughKinds.PlanCommitment:
            {
                if (request.On is not { } on)
                    return FollowThroughOperation<FollowThroughResult>.Invalid("on", "Pick the day.");
                if (on < today)
                    return FollowThroughOperation<FollowThroughResult>.Invalid("on", "Pick today or a later day.");
                var current = await commitments.GetAsync(targetId, ownerId, cancellationToken);
                if (current is null || current.Status != CommitmentStatuses.Open || current.Suggested)
                    return FollowThroughOperation<FollowThroughResult>.NotFound();
                // One existing record changes: its due day. The same day again changes nothing, so no second reminder.
                var updated = await commitments.UpdateAsync(targetId, ownerId, null, on, false, today, cancellationToken);
                return updated.Succeeded
                    ? FollowThroughOperation<FollowThroughResult>.Ok(new FollowThroughResult(new EntityRef(EntityTypes.Commitment, targetId).ToString()))
                    : FollowThroughOperation<FollowThroughResult>.Invalid(updated.Field ?? "on", updated.Message ?? "Could not change that.");
            }
            case FollowThroughKinds.ProjectNextStep:
            {
                var project = await projects.GetAsync(targetId, ownerId, cancellationToken);
                if (project is null) return FollowThroughOperation<FollowThroughResult>.NotFound();
                var hasLink = request.LinkType is not null && request.LinkId is not null;
                if (!hasLink && string.IsNullOrWhiteSpace(request.Note))
                    return FollowThroughOperation<FollowThroughResult>.Invalid("note", "Write the next step, or link a promise.");
                try
                {
                    await focus.SetAsync(ownerId, targetId, new ProjectFocusInput(project.Outcome, null,
                        new ProjectNextAction(hasLink ? request.LinkType : null, hasLink ? request.LinkId : null, request.Note)),
                        cancellationToken);
                }
                catch (ArgumentException exception)
                {
                    return FollowThroughOperation<FollowThroughResult>.Invalid(exception.ParamName ?? "note", exception.Message);
                }
                return FollowThroughOperation<FollowThroughResult>.Ok(new FollowThroughResult(new EntityRef(EntityTypes.Project, targetId).ToString()));
            }
            default:
                return FollowThroughOperation<FollowThroughResult>.Invalid("kind", "Unknown suggestion.");
        }
    }

    public async Task<bool> DismissAsync(Guid ownerId, string kind, string targetId, CancellationToken cancellationToken)
    {
        if (kind is not (FollowThroughKinds.PlanCommitment or FollowThroughKinds.ProjectNextStep) ||
            !Guid.TryParse(targetId, out _)) return false;
        var current = await settings.GetAsync<FollowThroughDismissals>(ownerId, FollowThroughDismissals.Section,
            cancellationToken) ?? new FollowThroughDismissals();
        await settings.SaveAsync(ownerId, FollowThroughDismissals.Section,
            current.With(WeekFollowThroughBuilder.Fingerprint(kind, targetId), clock.GetUtcNow()), cancellationToken);
        return true;
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        return (await Preferences.OwnerZoneResolver.ResolveAsync(settings, briefings, ownerId, cancellationToken)).Zone;
    }
}
