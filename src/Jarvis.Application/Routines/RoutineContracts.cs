using Jarvis.Application.Automations;
using Jarvis.Domain.Routines;

namespace Jarvis.Application.Routines;

public static class RoutineStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Dismissed = "dismissed";
}

public static class RoutineRules
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan NotifyInterval = TimeSpan.FromDays(7);
    public const int MaxSourceEvents = 5_000;
    public const string NotificationType = "routine.suggested";
}

/// <summary>When the miner last ran and last bothered the owner, kept in the owner's settings.</summary>
public sealed record RoutineState(DateTimeOffset? LastRefreshAt, DateTimeOffset? LastNotifiedAt);

/// <summary>A pending suggestion together with what its automation would do, without doing it.</summary>
public sealed record RoutineSuggestionView(
    Guid Id,
    string Title,
    string Evidence,
    double Confidence,
    AutomationSimulation Simulation,
    DateTimeOffset CreatedAt);

public sealed record RoutineAcceptResult(RoutineSuggestionEntry Suggestion, Guid AutomationId);

public interface IRoutineSuggestionRepository
{
    Task<IReadOnlyList<RoutineSuggestionEntry>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<RoutineSuggestionEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddRangeAsync(IReadOnlyList<RoutineSuggestionEntry> entries, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(RoutineSuggestionEntry entry, CancellationToken cancellationToken);
    Task<int> DeleteManyAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public interface IRoutineSuggestionService
{
    /// <summary>Pending suggestions, strongest first. Refreshes first when the last run is over a day old.</summary>
    Task<IReadOnlyList<RoutineSuggestionView>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Looks for routines now. <paramref name="force"/> skips the once-a-day throttle.</summary>
    Task<IReadOnlyList<RoutineSuggestionView>> RefreshAsync(Guid ownerId, bool force,
        CancellationToken cancellationToken);

    /// <summary>Creates the suggested automation as a draft (never enabled). Null when it does not exist.</summary>
    Task<RoutineAcceptResult?> AcceptAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<bool> DismissAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}
