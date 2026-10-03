using Jarvis.Domain.Missions;

namespace Jarvis.Application.Missions;

public static class MissionStatuses
{
    public const string Ready = "ready";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsFinished(string status) => status is Completed or Failed or Cancelled;
}

public static class StepStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    /// <summary>The owner chose to skip it; later steps carry on without its result.</summary>
    public const string Skipped = "skipped";

    /// <summary>Stopped by Jarvis: an earlier step failed, or the mission was cancelled.</summary>
    public const string Cancelled = "cancelled";

    public static bool IsDone(string status) => status is Completed or Skipped;
    public static bool IsFinal(string status) => status is Completed or Failed or Skipped or Cancelled;
}

public static class MissionRoles
{
    public const string Researcher = "researcher";
    public const string Planner = "planner";
    public const string Browser = "browser";
    public const string Coder = "coder";
    public const string Finance = "finance";
    public const string Writer = "writer";
    public const string Generalist = "generalist";

    public static readonly IReadOnlyList<string> All =
        [Researcher, Planner, Browser, Coder, Finance, Writer, Generalist];

    public static string Normalize(string? role)
    {
        var key = role?.Trim().ToLowerInvariant();
        return key is not null && All.Contains(key) ? key : Generalist;
    }

    public static string Guidance(string role) => role switch
    {
        Researcher => "You are the researcher. Find facts with web search and the user's library; name your sources; say what you could not confirm.",
        Planner => "You are the planner. Turn the information you are given into a concrete, ordered plan with times, costs and trade-offs.",
        Browser => "You are the browser operator. Use the browsing tools to look things up on specific sites, and report exactly what you found, with links.",
        Coder => "You are the engineer. Only use coding tools if they are available; otherwise explain what should be built and how.",
        Finance => "You are the finance specialist. Use the user's budgets, spending and subscriptions to work out what is affordable; show your arithmetic.",
        Writer => "You are the writer. Turn the material you are given into clear, well-organised text in the user's language.",
        _ => "You are a capable generalist on this team."
    };
}

public static class MissionRules
{
    public const int MaxSteps = 8;
    public const int MaxParallel = 3;
    public const int MaxActiveMissions = 5;
    public const int MaxGoalLength = 2_000;
    public const int MaxTitleLength = 100;
    public const int MaxInstructionLength = 2_000;
    public const int MaxStepTitleLength = 100;
    public const int MaxResultLength = 6_000;
    public const int MaxContextPerStep = 3_000;
    public const int MaxContextTotal = 12_000;
    public const int MaxNotes = 40;
    public const int MaxNoteKeyLength = 60;
    public const int MaxNoteValueLength = 1_500;

    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? Limit(string? value, int max)
    {
        var clean = Clean(value);
        return clean is null ? null : clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }

    public static string ClipText(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }
}

public sealed record PlannedStep(string Key, string Role, string Title, string Instruction,
    IReadOnlyList<string> DependsOn);

public sealed record MissionPlan(string Title, IReadOnlyList<PlannedStep> Steps);

public interface IMissionPlanner
{
    /// <summary>Splits a goal into steps. Never throws for a bad model answer; it falls back to one step.</summary>
    Task<MissionPlan> PlanAsync(Guid ownerId, string goal, CancellationToken cancellationToken);
}

public sealed record MissionDetail(
    Mission Mission,
    IReadOnlyList<MissionStep> Steps,
    IReadOnlyList<MissionNote> Notes,
    IReadOnlyDictionary<Guid, string> TaskStatuses);

public enum MissionFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

public sealed record MissionOperation<T>(T? Value, MissionFailure Failure = MissionFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == MissionFailure.None;

    public static MissionOperation<T> Ok(T value) => new(value);
    public static MissionOperation<T> NotFound() => new(default, MissionFailure.NotFound);
    public static MissionOperation<T> Invalid(string field, string message) =>
        new(default, MissionFailure.Invalid, field, message);
    public static MissionOperation<T> Conflict(string message) => new(default, MissionFailure.Conflict, null, message);
}

public interface IMissionRepository
{
    Task<IReadOnlyList<Mission>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Mission?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<Mission?> GetForSupervisorAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListRunningIdsAsync(CancellationToken cancellationToken);
    Task<int> CountActiveAsync(Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(Mission mission, IReadOnlyList<MissionStep> steps, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Mission mission, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MissionStep>> ListStepsAsync(Guid missionId, CancellationToken cancellationToken);
    Task<MissionStep?> GetStepAsync(Guid stepId, Guid ownerId, CancellationToken cancellationToken);
    Task<MissionStep?> FindStepByTaskAsync(Guid taskId, CancellationToken cancellationToken);
    Task<bool> UpdateStepAsync(MissionStep step, CancellationToken cancellationToken);

    /// <summary>Moves a step from pending to running; false when another supervisor already took it.</summary>
    Task<bool> TryClaimStepAsync(Guid stepId, DateTimeOffset at, CancellationToken cancellationToken);

    Task<IReadOnlyList<MissionNote>> ListNotesAsync(Guid missionId, CancellationToken cancellationToken);
    Task UpsertNoteAsync(MissionNote note, CancellationToken cancellationToken);
}

public interface IMissionService
{
    Task<IReadOnlyList<Mission>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MissionDetail?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Plans a mission from a goal. It waits in "ready" until the owner starts it.</summary>
    Task<MissionOperation<MissionDetail>> CreateAsync(Guid ownerId, string? goal, string? title, Guid? projectId,
        CancellationToken cancellationToken);

    Task<MissionOperation<MissionDetail>> StartAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<MissionOperation<MissionDetail>> PauseAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<MissionOperation<MissionDetail>> ResumeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<MissionOperation<MissionDetail>> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<MissionOperation<MissionDetail>> UpdateStepAsync(Guid stepId, Guid ownerId, string? instruction,
        CancellationToken cancellationToken);
    Task<MissionOperation<MissionDetail>> SkipStepAsync(Guid stepId, Guid ownerId, CancellationToken cancellationToken);
    Task<MissionOperation<MissionDetail>> RetryStepAsync(Guid stepId, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>One supervisor pass over a running mission: collect finished steps, start the ready ones.</summary>
    Task AdvanceAsync(Guid missionId, CancellationToken cancellationToken);

    /// <summary>One pass over every running mission.</summary>
    Task<int> AdvanceAllAsync(CancellationToken cancellationToken);

    /// <summary>The blackboard of the mission the running task belongs to; null when it is not part of one.</summary>
    Task<IReadOnlyList<MissionNote>?> ReadNotesAsync(Guid taskId, CancellationToken cancellationToken);
    Task<bool> PostNoteAsync(Guid taskId, string key, string value, CancellationToken cancellationToken);
}
