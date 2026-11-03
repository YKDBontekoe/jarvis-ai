namespace Jarvis.Application.Proactivity;

public static class ProactiveKinds
{
    public static readonly IReadOnlyList<string> All =
    ["open_loop", "project", "deadline", "day_plan", "follow_through", "meeting", "return", "refund", "repair",
        "goal_capacity", "trip", "consistency", "context", "decision", "research", "memory_conflict", "routine_trial"];
}

public sealed record ProactiveStep(string Id, string Title, IReadOnlyList<string> DependsOn,
    string? SourceRef = null, int? Minutes = null, string? CompletionCondition = null, bool Done = false);

public sealed record ProactiveAssumption(string Id, string Statement, string SourceRef, string? ExpectedVersion = null,
    DateTimeOffset? ReviewAt = null, string? Comparison = null, double? Threshold = null, string? AcknowledgedVersion = null,
    string? Effect = null, string? TripRef = null, string? TripItemId = null,
    DateOnly? BaselineDate = null, TimeOnly? BaselineStart = null, TimeOnly? BaselineEnd = null);

public sealed record ProactivePolicy(int CadenceMinutes = 60, int MaxChecksPerDay = 12,
    string TimeZoneId = "UTC", int QuietStart = 22, int QuietEnd = 8, int OptionalItemsPerDay = 3,
    bool UseCalendarCapacity = false);

public sealed record ProactiveCaseDraft(string Kind, string Title, string Objective,
    IReadOnlyList<string>? SourceRefs = null, string? RootRef = null, Guid? ProfileId = null,
    bool Explicit = true, bool Enabled = false, string? StopCondition = null, ProactivePolicy? Policy = null,
    DateTimeOffset? Deadline = null, int? AvailableMinutes = null,
    IReadOnlyList<ProactiveStep>? Steps = null, IReadOnlyList<ProactiveAssumption>? Assumptions = null,
    DateTimeOffset? ExpiresAt = null, string? Notes = null);

public sealed record ProactiveSource(string Ref, string Title, string Version, string State, string Detail,
    DateTimeOffset? At = null, string? ParentRef = null, bool Protected = false, string? Url = null,
    string? OriginVersion = null, bool IsPinned = false, double? NumericValue = null,
    int? EffortMinutes = null, IReadOnlyList<Trips.TripOption>? TripOptions = null,
    IReadOnlyList<Trips.TripItem>? TripItems = null, DateTimeOffset? ValidUntil = null);

public sealed record ProactiveFinding(string Key, string Title, string Reason, string? SourceRef = null,
    string Severity = "review");

public sealed record ProactiveFeedback(Guid Id, string Signal, DateTimeOffset At, Guid? ProfileId);

public sealed record ProactiveReceipt(Guid Id, string Kind, string Reason, string Before, string After,
    DateTimeOffset At, long AppliedRevision, bool Undone = false, string? Trigger = null,
    IReadOnlyDictionary<string, string>? EvidenceVersions = null);

public sealed record ProactiveReportVersion(DateTimeOffset At, string Content, IReadOnlyDictionary<string, string> Sources);

public sealed record ProactiveCase(
    Guid Id, Guid OwnerId, string Fingerprint, string Kind, string Title, string Objective,
    string Status, bool Enabled, Guid? ProfileId, string? RootRef, IReadOnlyList<string> SourceRefs,
    string? StopCondition, ProactivePolicy Policy, IReadOnlyList<ProactiveStep> Steps,
    IReadOnlyList<ProactiveAssumption> Assumptions, DateTimeOffset? Deadline, int? AvailableMinutes,
    string NextAction, IReadOnlyList<ProactiveSource> Context, IReadOnlyList<ProactiveFinding> Findings,
    IReadOnlyList<ProactiveReceipt> Receipts, IReadOnlyList<ProactiveFeedback> Feedback,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastCheckedAt,
    DateTimeOffset? NextCheckAt, long Revision = 1, int Failures = 0, string? FailureKind = null,
    Guid? RunId = null, DateTimeOffset? LeaseUntil = null, DateOnly? BudgetDay = null, int ChecksToday = 0,
    string? PreparedReport = null, IReadOnlyList<ProactiveTrialObservation>? TrialObservations = null,
    DateTimeOffset? ExpiresAt = null, string? Notes = null, string? LastTrigger = null,
    IReadOnlyList<ProactiveReportVersion>? Reports = null, IReadOnlyList<ProactiveClaimReview>? ClaimReviews = null)
{
    public bool Terminal => Status is "resolved" or "dismissed" or "stopped";
}

public sealed record ProactiveEvaluation(string NextAction, IReadOnlyList<ProactiveFinding> Findings,
    IReadOnlyList<ProactiveStep> Steps);

public sealed record ProactiveTrialObservation(Guid EventId, DateTimeOffset At, int Actions, int Approvals, string Reason);
public sealed record ProactiveSettingMutation(string Section, string? ExpectedJson, string AfterJson);
public sealed record ProactivePreparation(IReadOnlyList<ProactiveSource> Sources, string? Report,
    IReadOnlyList<ProactiveTrialObservation>? Observations = null, ProactiveSettingMutation? Mutation = null,
    IReadOnlyList<ProactiveClaimReview>? ClaimReviews = null);

public sealed record ProactiveClaimEvidence(string Id, string Statement, string SourceRef, string SourceUrl, string SourceVersion, string Evidence);
public sealed record ProactiveClaimReview(string Id, string Status, string Reason, string? Quote,
    string SourceRef, string SourceVersion, string ClaimHash);
public interface IProactiveClaimReviewer
{
    Task<IReadOnlyList<ProactiveClaimReview>> ReviewAsync(Guid ownerId, IReadOnlyList<ProactiveClaimEvidence> claims, CancellationToken ct);
}

public interface IProactivePreparationService
{
    Task<ProactivePreparation> PrepareAsync(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, CancellationToken ct);
}

public interface IProactiveCaseScheduler
{
    Task ScheduleProactiveAsync(Guid ownerId, Guid caseId, CancellationToken ct);
}

public interface IOwnerSettingDocuments
{
    Task<string?> ReadDocumentAsync(Guid ownerId, string section, CancellationToken ct);
}

public sealed record ProactiveCapacity(DateOnly WeekStart, int? TotalMinutes, int? RemainingMinutes,
    int ConsumedMinutes, string Explanation, DateTimeOffset CheckedAt);
public interface IProactiveCapacityService
{
    Task<ProactiveCapacity> CalculateAsync(ProactiveCase value, CancellationToken ct);
}

public sealed record ProactiveAttention(string Id, string Title, string Reason, string Destination, string GroupKey,
    bool Locked);

public interface IProactiveRepository
{
    Task<ProactiveCase> CreateAsync(ProactiveCase value, CancellationToken ct);
    Task<ProactiveCase?> GetAsync(Guid ownerId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProactiveCase>> ListAsync(Guid ownerId, CancellationToken ct);
    Task<IReadOnlyList<ProactiveCase>> DueAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> CompareExchangeAsync(ProactiveCase value, long expectedRevision, CancellationToken ct);
    Task<bool> ApplyAsync(ProactiveCase value, long expectedRevision, ProactiveSettingMutation mutation, CancellationToken ct);
}

public interface IProactiveSourceReader
{
    Task<bool> AllowsProfileAsync(Guid ownerId, Guid? profileId, CancellationToken ct) => Task.FromResult(profileId is null);
    Task<IReadOnlyList<ProactiveSource>> AvailableAsync(Guid ownerId, CancellationToken ct);
    /// <summary>Missing, deleted and profile-inaccessible sources are omitted on every read.</summary>
    Task<IReadOnlyList<ProactiveSource>> ReadAsync(Guid ownerId, Guid? profileId,
        IReadOnlyList<string> refs, CancellationToken ct);
}

public interface IProactiveService
{
    Task<ProactiveCase> CreateAsync(Guid ownerId, ProactiveCaseDraft draft, CancellationToken ct);
    Task<IReadOnlyList<ProactiveCase>> ListAsync(Guid ownerId, CancellationToken ct);
    Task<ProactiveCase?> GetAsync(Guid ownerId, Guid id, CancellationToken ct);
    Task<ProactiveCase?> UpdateAsync(Guid ownerId, Guid id, ProactiveCaseDraft draft, long revision, CancellationToken ct);
    Task<ProactiveCase?> CommandAsync(Guid ownerId, Guid id, string command, long revision, CancellationToken ct);
    Task<ProactiveCase?> FeedbackAsync(Guid ownerId, Guid id, string signal, long revision, CancellationToken ct);
    Task<ProactiveCase?> UndoAsync(Guid ownerId, Guid id, Guid receiptId, long revision, CancellationToken ct);
    Task<ProactiveCase?> CheckAsync(Guid ownerId, Guid id, CancellationToken ct);
}

public sealed class ProactiveConflictException() : Exception("This work changed. Refresh before trying again.");
