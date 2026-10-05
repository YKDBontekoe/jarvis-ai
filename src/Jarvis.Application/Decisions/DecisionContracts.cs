using Jarvis.Domain.Decisions;

namespace Jarvis.Application.Decisions;

public static class DecisionStatuses
{
    public const string Open = "open";
    public const string Due = "due";
    public const string Resolved = "resolved";

    public static readonly IReadOnlyList<string> All = [Open, Due, Resolved];
}

public static class DecisionRules
{
    public const int MaxTitleLength = 200;
    public const int MaxContextLength = 2_000;
    public const int MaxPredictionLength = 500;
    public const int MaxNoteLength = 1_000;
    public const double MinProbability = 0.01;
    public const double MaxProbability = 0.99;
    public const int MaxUnresolved = 200;
    public const int MaxHorizonDays = 700;
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public static readonly TimeOnly ReminderTime = new(9, 0);

    public static string StatusOf(Decision decision, DateOnly today) =>
        decision.IsResolved ? DecisionStatuses.Resolved
        : decision.ReviewOn <= today ? DecisionStatuses.Due : DecisionStatuses.Open;
}

public sealed record CreateDecisionRequest(string Title, string Prediction, double Probability, DateOnly ReviewOn,
    string? Context = null);

/// <summary>Only the fields that are set change, and only while the decision is unresolved.</summary>
public sealed record UpdateDecisionRequest(string? Title = null, string? Context = null, string? Prediction = null,
    double? Probability = null, DateOnly? ReviewOn = null);

public sealed record DecisionView(
    Guid Id,
    string Title,
    string? Context,
    string Prediction,
    double Probability,
    DateOnly ReviewOn,
    string Status,
    bool? Outcome,
    string? OutcomeNote,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);

public interface IDecisionRepository
{
    /// <summary>All of the owner's decisions, newest review date first.</summary>
    Task<IReadOnlyList<Decision>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Decision?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<int> CountUnresolvedAsync(Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(Decision decision, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Decision decision, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Decisions logged or resolved between the two moments, for the life timeline.</summary>
    Task<IReadOnlyList<Decision>> ListActiveBetweenAsync(Guid ownerId, DateTimeOffset from, DateTimeOffset to,
        int limit, CancellationToken cancellationToken);
}

public interface IDecisionService
{
    /// <exception cref="ArgumentException">A field is missing or out of range.</exception>
    Task<DecisionView> CreateAsync(Guid ownerId, CreateDecisionRequest request, CancellationToken cancellationToken);

    /// <returns>Null when the decision does not exist.</returns>
    Task<DecisionView?> UpdateAsync(Guid id, Guid ownerId, UpdateDecisionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Records whether the prediction came true. Resolving again overwrites the earlier answer.</summary>
    Task<DecisionView?> ResolveAsync(Guid id, Guid ownerId, bool outcome, string? note,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<DecisionView?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <param name="status">open, due or resolved; null for all.</param>
    Task<IReadOnlyList<DecisionView>> ListAsync(Guid ownerId, string? status, int limit,
        CancellationToken cancellationToken);

    Task<CalibrationReport> CalibrationAsync(Guid ownerId, CancellationToken cancellationToken);
}
