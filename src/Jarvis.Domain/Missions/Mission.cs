namespace Jarvis.Domain.Missions;

/// <summary>
/// A big job split into steps that specialist agents work on, in parallel where they do not depend on each other.
/// <see cref="Status"/> runs ready (planned, waiting for the owner), running, paused, then completed, failed or
/// cancelled. <see cref="Summary"/> is the result of the final step.
/// </summary>
public sealed record Mission(
    Guid Id,
    Guid OwnerId,
    string Title,
    string Goal,
    string Status,
    Guid? ProjectId,
    string? Summary,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// One step of a mission, done by one background task. <see cref="DependsOn"/> lists the keys of earlier steps whose
/// results this step needs. <see cref="Result"/> is what the task answered, which can include web text: untrusted.
/// </summary>
public sealed record MissionStep(
    Guid Id,
    Guid MissionId,
    Guid OwnerId,
    string Key,
    int Ordinal,
    string Role,
    string Title,
    string Instruction,
    IReadOnlyList<string> DependsOn,
    string Status,
    Guid? TaskId,
    string? Result,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>A fact one step leaves on the mission's shared blackboard for the steps after it.</summary>
public sealed record MissionNote(
    Guid Id,
    Guid MissionId,
    Guid OwnerId,
    string Key,
    string Value,
    string? StepKey,
    DateTimeOffset UpdatedAt);
