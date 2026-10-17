namespace Jarvis.Application.Goals;

public static class GoalStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Achieved = "achieved";
    public const string Archived = "archived";

    public static readonly string[] All = [Active, Paused, Achieved, Archived];
}

public static class GoalFeelings
{
    public const string OnTrack = "on_track";
    public const string Slipping = "slipping";
    public const string Stuck = "stuck";

    public static readonly string[] All = [OnTrack, Slipping, Stuck];
}

public static class GoalPlanStatuses
{
    public const string Proposed = "proposed";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Done = "done";
}

public static class GoalRules
{
    public const int MaxGoals = 20;
    public const int MaxTitle = 200;
    public const int MaxText = 600;
    public const int MaxHabits = 5;
    public const int MaxCommitments = 8;
    public const int MaxCheckIns = 200;
    public const int MaxExperiments = 10;
    public const int MaxNotes = 60;
    public const int MinCheckInDays = 3;
    public const int MaxCheckInDays = 60;
    public const int DefaultCheckInDays = 7;
    public const int MinWeeklyMinutes = 15;
    public const int MaxWeeklyMinutes = 1_200;
    public const int DefaultWeeklyMinutes = 90;
    public const int MaxPlanItems = 3;
    public const int ProposalMinutes = 30;
    public const int HabitSessionMinutes = 20;

    /// <summary>A declined suggestion, or a dismissed review, stays quiet this long.</summary>
    public const int QuietDays = 28;

    public const int MinExperimentDays = 3;
    public const int MaxExperimentDays = 60;
}

/// <summary>One check-in the owner wrote: what moved, what got in the way, what to change.</summary>
public sealed record GoalCheckIn(
    Guid Id,
    DateTimeOffset At,
    string Progress,
    string? Friction,
    string? Adjustment,
    string Feeling,
    int Season = 1);

public sealed record GoalPlanItem(Guid Id, string Title, int Minutes, string Status, string? Ref, string Key);

/// <summary>A small plan for one week (Monday as <see cref="WeekStart"/>). Nothing is scheduled until accepted.</summary>
public sealed record GoalPlan(DateOnly WeekStart, IReadOnlyList<GoalPlanItem> Items, int OmittedForTime = 0);

public sealed record ExperimentNote(DateOnly On, string Text);

public static class ExperimentStatuses
{
    public const string Running = "running";
    public const string Kept = "kept";
    public const string Stopped = "stopped";
}

/// <summary>One bounded change with a review day. Notes are the owner's observations, never causes.</summary>
public sealed record GoalExperiment(
    Guid Id,
    string Change,
    string ExpectedBenefit,
    int DurationDays,
    DateOnly StartOn,
    DateOnly ReviewOn,
    IReadOnlyList<ExperimentNote> Notes,
    string Status,
    string? Conclusion);

public sealed record GoalRecord(
    Guid Id,
    string Title,
    string? Motivation,
    DateOnly? Horizon,
    string? Target,
    int CheckInEveryDays,
    int WeeklyMinutes,
    string Status,
    Guid? ProjectId,
    IReadOnlyList<Guid> HabitIds,
    IReadOnlyList<Guid> CommitmentIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<GoalCheckIn> CheckIns,
    GoalPlan? Plan,
    IReadOnlyList<GoalExperiment> Experiments,
    /// <summary>Suggestion key to the day it was declined, so it is not offered again for a while.</summary>
    IReadOnlyDictionary<string, DateOnly> Declined,
    DateOnly? ReviewDismissedOn = null,
    int Season = 1);

/// <summary>Owner settings section <see cref="Section"/>: every goal the owner keeps.</summary>
public sealed record GoalState(IReadOnlyList<GoalRecord> Goals)
{
    public const string Section = "goals";
}

public sealed record GoalInput(
    string? Title,
    string? Motivation,
    DateOnly? Horizon,
    string? Target,
    int? CheckInEveryDays,
    int? WeeklyMinutes,
    string? Status,
    Guid? ProjectId,
    IReadOnlyList<Guid>? HabitIds,
    IReadOnlyList<Guid>? CommitmentIds,
    bool ClearProject = false,
    bool ClearHorizon = false);

public sealed record CheckInInput(string? Progress, string? Friction, string? Adjustment, string? Feeling);

public sealed record ExperimentInput(string? Change, string? ExpectedBenefit, int? DurationDays);

public sealed record PlanItemAction(string Action, int? Minutes);

public sealed record GoalHabitView(Guid Id, string Name, int ThisWeekCount, int TargetPerWeek, int CurrentStreak,
    bool Archived);

public sealed record GoalProjectView(Guid Id, string Name, string? Outcome, string? NextAction);

public sealed record GoalCommitmentView(Guid Id, string Description, string Counterparty, string Status, DateOnly? DueOn);

/// <summary>A keep/change/stop suggestion with the check-ins it rests on. Rule-based, never a diagnosis.</summary>
public sealed record GoalReviewSuggestion(string Verdict, string Reason, IReadOnlyList<Guid> BasedOn);

public static class GoalVerdicts
{
    public const string Keep = "keep";
    public const string Change = "change";
    public const string Stop = "stop";
}

/// <summary>What the owner sees, labelled by where it comes from: measured, written by the owner, or inferred.</summary>
public sealed record GoalProgressView(
    IReadOnlyList<string> Measured,
    IReadOnlyList<GoalCheckIn> Reflections,
    GoalReviewSuggestion? Interpretation);

public sealed record GoalView(
    GoalRecord Goal,
    bool CheckInDue,
    int? DaysSinceCheckIn,
    IReadOnlyList<GoalHabitView> Habits,
    GoalProjectView? Project,
    IReadOnlyList<GoalCommitmentView> Commitments,
    GoalProgressView Progress);

public sealed record GoalSummary(
    Guid Id,
    string Title,
    string Status,
    bool CheckInDue,
    int CheckInCount,
    DateTimeOffset? LastCheckInAt,
    string? Feeling,
    GoalReviewSuggestion? Review,
    int OpenPlanItems);

public enum GoalFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

public sealed record GoalOperation<T>(T? Value, GoalFailure Failure = GoalFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == GoalFailure.None;
    public static GoalOperation<T> Ok(T value) => new(value);
    public static GoalOperation<T> NotFound() => new(default, GoalFailure.NotFound);
    public static GoalOperation<T> Invalid(string field, string message) =>
        new(default, GoalFailure.Invalid, field, message);
    public static GoalOperation<T> Conflict(string field, string message) =>
        new(default, GoalFailure.Conflict, field, message);
}

public interface IGoalService
{
    Task<IReadOnlyList<GoalSummary>> ListAsync(Guid ownerId, bool includeArchived, CancellationToken cancellationToken);
    Task<GoalView?> GetAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken);
    Task<GoalOperation<GoalView>> CreateAsync(Guid ownerId, GoalInput input, CancellationToken cancellationToken);
    Task<GoalOperation<GoalView>> UpdateAsync(Guid ownerId, Guid goalId, GoalInput input,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken);

    /// <summary>Starts a new season: the history stays, progress and plan begin again.</summary>
    Task<GoalOperation<GoalView>> ResetSeasonAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken);

    Task<GoalOperation<GoalView>> AddCheckInAsync(Guid ownerId, Guid goalId, CheckInInput input,
        CancellationToken cancellationToken);

    /// <summary>Proposes this week's small plan; accepted and done items are kept, proposals are replaced.</summary>
    Task<GoalOperation<GoalView>> ProposePlanAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken);

    Task<GoalOperation<GoalView>> ActOnPlanItemAsync(Guid ownerId, Guid goalId, Guid itemId, PlanItemAction action,
        string? timeZoneId, CancellationToken cancellationToken);

    Task<GoalOperation<GoalView>> StartExperimentAsync(Guid ownerId, Guid goalId, ExperimentInput input,
        CancellationToken cancellationToken);

    Task<GoalOperation<GoalView>> NoteExperimentAsync(Guid ownerId, Guid goalId, Guid experimentId, string? text,
        CancellationToken cancellationToken);

    Task<GoalOperation<GoalView>> FinishExperimentAsync(Guid ownerId, Guid goalId, Guid experimentId, bool keep,
        string? conclusion, CancellationToken cancellationToken);

    /// <summary>"Not now" for the keep/change/stop suggestion: quiet for <see cref="GoalRules.QuietDays"/> days.</summary>
    Task<GoalOperation<GoalView>> DismissReviewAsync(Guid ownerId, Guid goalId, CancellationToken cancellationToken);
}
