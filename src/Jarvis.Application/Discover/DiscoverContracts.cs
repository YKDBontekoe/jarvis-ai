using Jarvis.Domain.Discover;

namespace Jarvis.Application.Discover;

// ---- collisions ----

/// <summary>One thing from the owner's own records that takes part in a collision.</summary>
public sealed record CollisionItem(string Kind, Guid Id, string Title, string Text, DateTimeOffset At);

public sealed record CollisionSide(string Kind, Guid Id, string Title, string Excerpt, DateOnly On);

/// <summary>Two unrelated things the owner saved, wrote or told Jarvis, linked by words both use.</summary>
public sealed record Collision(string Key, CollisionSide Left, CollisionSide Right, IReadOnlyList<string> Shared,
    string Prompt);

public sealed record CollisionState(DateOnly? Day = null, string? Key = null, IReadOnlyList<string>? Seen = null);

public interface ICollisionService
{
    /// <summary>Today's collision; the same one all day until the owner asks for another. Null when none is found.</summary>
    Task<Collision?> TodayAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Puts today's collision away and finds another.</summary>
    Task<Collision?> NextAsync(Guid ownerId, CancellationToken cancellationToken);
}

// ---- life museum ----

public sealed record MuseumExhibit(string Title, string? Detail, string? Value, DateOnly? On);

public sealed record MuseumRoom(string Key, string Title, string Caption, IReadOnlyList<MuseumExhibit> Exhibits);

public sealed record Museum(int Year, IReadOnlyList<MuseumRoom> Rooms, IReadOnlyList<int> Years);

public interface IMuseumService
{
    /// <exception cref="ArgumentException">The year is outside the last ten.</exception>
    Task<Museum> BuildAsync(Guid ownerId, int? year, CancellationToken cancellationToken);
}

// ---- duels ----

public static class DuelMetrics
{
    public const string HabitCheckIns = "habit_checkins";
    public const string JournalDays = "journal_days";
    public const string PromisesKept = "promises_kept";

    public static readonly IReadOnlyList<string> All = [HabitCheckIns, JournalDays, PromisesKept];

    public static string Label(string metric) => metric switch
    {
        HabitCheckIns => "Habit check-ins",
        JournalDays => "Days journalled",
        PromisesKept => "Promises kept",
        _ => metric
    };
}

public static class DuelRoles
{
    public const string Challenger = "challenger";
    public const string Joiner = "joiner";

    public static string Other(string role) => role == Challenger ? Joiner : Challenger;
}

public static class DuelStatuses
{
    public const string Active = "active";
    public const string Finished = "finished";
    public const string Cancelled = "cancelled";
}

public static class DuelRules
{
    public const int MinDays = 3;
    public const int MaxDays = 14;
    public const int MaxActive = 5;
    public const int CodeLength = 8;

    /// <summary>The friend's score is asked for at most this often.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);
}

public sealed record DuelView(
    Guid Id,
    string Code,
    string Role,
    string PeerName,
    string Metric,
    string MetricLabel,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    int MyScore,
    int? TheirScore,
    DateTimeOffset? TheirScoreAt,
    string? Outcome,
    string? LastError);

/// <summary>What a friend's Jarvis tells us about a duel: never more than these fields.</summary>
public sealed record PeerDuelTerms(string Metric, DateOnly StartsOn, DateOnly EndsOn);

public sealed record DuelOperation(DuelView? Value, string? Field = null, string? Error = null)
{
    public static DuelOperation Invalid(string field, string error) => new(null, field, error);
}

public interface IDuelRepository
{
    Task<IReadOnlyList<Duel>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Duel?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<Duel?> FindAsync(Guid ownerId, string code, string role, CancellationToken cancellationToken);
    Task AddAsync(Duel duel, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Duel duel, CancellationToken cancellationToken);
}

/// <summary>Talks to a friend's Jarvis over Agent2Agent with the token saved for it under Settings → Agents.</summary>
public interface IDuelPeerClient
{
    /// <exception cref="DuelPeerException">The friend's Jarvis could not be reached or refused.</exception>
    Task<PeerDuelTerms> GetTermsAsync(Guid ownerId, Guid remoteAgentId, string code,
        CancellationToken cancellationToken);

    /// <exception cref="DuelPeerException">The friend's Jarvis could not be reached or refused.</exception>
    Task<int> GetScoreAsync(Guid ownerId, Guid remoteAgentId, string code, string myRole,
        CancellationToken cancellationToken);
}

public sealed class DuelPeerException(string message) : Exception(message);

public interface IDuelService
{
    Task<IReadOnlyList<DuelView>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<DuelOperation> ChallengeAsync(Guid ownerId, Guid remoteAgentId, string metric, int days,
        CancellationToken cancellationToken);
    Task<DuelOperation> JoinAsync(Guid ownerId, Guid remoteAgentId, string code, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>For a friend's Jarvis: the terms of the owner's challenge with this code, or null.</summary>
    Task<PeerDuelTerms?> TermsForPeerAsync(Guid ownerId, string code, CancellationToken cancellationToken);

    /// <summary>For a friend's Jarvis: the owner's own score in the duel the friend holds the other side of.</summary>
    Task<int?> ScoreForPeerAsync(Guid ownerId, string code, string peerRole, CancellationToken cancellationToken);
}
