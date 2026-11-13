using Jarvis.Domain.Game;

namespace Jarvis.Application.Game;

public static class BetKinds
{
    /// <summary>"You will skip a daily habit on one day."</summary>
    public const string HabitDay = "habit_day";

    /// <summary>"You will not reach a weekly habit's target this week."</summary>
    public const string HabitWeek = "habit_week";

    /// <summary>"You will go over a budget this month."</summary>
    public const string Budget = "budget";
}

public static class BetStatuses
{
    public const string Open = "open";

    /// <summary>The owner proved Jarvis wrong.</summary>
    public const string Won = "won";

    /// <summary>Jarvis was right.</summary>
    public const string Lost = "lost";

    /// <summary>Called off by the owner, or what it was about is gone.</summary>
    public const string Void = "void";
}

public static class QuestKinds
{
    public const string Promise = "promise";
    public const string Person = "person";
    public const string Habit = "habit";
    public const string Journal = "journal";
}

public static class QuestStatuses
{
    public const string Open = "open";
    public const string Done = "done";
    public const string Skipped = "skipped";
}

public static class GameRules
{
    public const int MaxOpenBets = 3;

    /// <summary>A subject that was just bet on (or called off) is left alone for this long.</summary>
    public const int SubjectCooldownDays = 7;

    public const int RegularQuests = 3;
    public const int QuestPoints = 30;
    public const int JournalQuestPoints = 20;
    public const int BossPoints = 100;

    /// <summary>The ghost is the owner on the same weekdays this many days earlier.</summary>
    public const int GhostLagDays = 28;

    public const string NotificationType = "game";

    /// <summary>Beating a confident Jarvis pays more: 20 points at 50% up to 92 at 95%.</summary>
    public static int PointsFor(double confidence) =>
        (int)Math.Round(20 + 160 * (Math.Clamp(confidence, 0.5, 0.95) - 0.5));

    private static readonly string[] Titles =
        ["Rookie", "Contrarian", "Spoiler", "Plot twist", "Wildcard", "Unpredictable", "Legend"];

    /// <summary>Level L starts at 50·(L−1)·L points: 0, 100, 300, 600, 1000…</summary>
    public static int LevelStart(int level) => 50 * (level - 1) * level;

    public static int LevelFor(int points)
    {
        var level = 1;
        while (LevelStart(level + 1) <= Math.Max(0, points)) level++;
        return level;
    }

    public static string TitleFor(int level) => Titles[Math.Clamp(level - 1, 0, Titles.Length - 1)];

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}

// ---- inputs to the pure engines ----

/// <summary>A habit with the days it was done; <paramref name="Paused"/> says whether a day was paused.</summary>
public sealed record HabitHistory(
    Guid Id,
    string Name,
    string Cadence,
    int TargetPerWeek,
    DateOnly CreatedOn,
    IReadOnlySet<DateOnly> Dates,
    Func<DateOnly, bool> Paused);

public sealed record BudgetSnapshot(string Category, decimal Limit, decimal Spent, decimal? Projected,
    string Currency);

public sealed record BetCandidate(
    string Kind,
    string SubjectKey,
    Guid? SubjectId,
    string Claim,
    string Evidence,
    double Confidence,
    DateOnly StartsOn,
    DateOnly EndsOn);

public sealed record QuestCandidate(
    string Kind,
    string SubjectKey,
    Guid? SubjectId,
    string Title,
    string Detail,
    int Target,
    int Points,
    bool IsBoss);

/// <summary>An open promise the owner made.</summary>
public sealed record PromiseInput(Guid Id, string Counterparty, string Description, DateOnly? DueOn,
    DateOnly CreatedOn);

/// <summary>Someone the relationship radar is worried about, with its most serious signal.</summary>
public sealed record DriftInput(Guid PersonId, string Name, int Severity, string Headline);

/// <summary>One measurement in the race against the ghost.</summary>
public sealed record GhostMetric(
    string Key,
    string Label,
    double Now,
    double Ghost,
    string Unit,
    bool LowerIsBetter,
    string Verdict);

public static class GhostVerdicts
{
    public const string Ahead = "ahead";
    public const string Behind = "behind";
    public const string Even = "even";
}

public sealed record GhostReport(
    DateOnly From,
    DateOnly To,
    DateOnly GhostFrom,
    DateOnly GhostTo,
    IReadOnlyList<GhostMetric> Metrics,
    int Ahead,
    int Behind);

// ---- views ----

public sealed record GameScore(
    int Points,
    int Level,
    string Title,
    int LevelStart,
    int NextLevelAt,
    int BetsWon,
    int BetsLost,
    int QuestsDone,
    int BossesBeaten);

public sealed record GameBetView(
    Guid Id,
    string Kind,
    string Claim,
    string Evidence,
    int ConfidencePercent,
    int Points,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    DateTimeOffset? ResolvedAt);

public sealed record GameQuestView(
    Guid Id,
    string Kind,
    string Title,
    string Detail,
    int Progress,
    int Target,
    int Points,
    bool IsBoss,
    string Status,
    DateTimeOffset? CompletedAt,
    Guid? SubjectId);

public sealed record GameBoard(
    DateOnly Today,
    GameScore Score,
    IReadOnlyList<GameBetView> Bets,
    IReadOnlyList<GameQuestView> Quests,
    GhostReport Ghost);

public sealed record GameRefreshResult(int BetsPlaced, int BetsSettled, int QuestsCreated, int QuestsCompleted);

public sealed record GameState(DateOnly? LastBetNotifiedOn = null);

public interface IGameRepository
{
    Task<IReadOnlyList<GameBet>> ListBetsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<GameBet?> GetBetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddBetsAsync(IReadOnlyList<GameBet> bets, CancellationToken cancellationToken);
    Task<bool> UpdateBetAsync(GameBet bet, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameQuest>> ListQuestsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<GameQuest?> GetQuestAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddQuestsAsync(IReadOnlyList<GameQuest> quests, CancellationToken cancellationToken);
    Task<bool> UpdateQuestAsync(GameQuest quest, CancellationToken cancellationToken);
}

public interface IGameService
{
    /// <summary>Settles what can be settled, places new bets and the week's quests, then returns the board.</summary>
    Task<GameBoard> BoardAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<GameRefreshResult> RefreshAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <returns>Null when the quest does not exist or is no longer open.</returns>
    Task<GameQuestView?> CompleteQuestAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<GameQuestView?> SkipQuestAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Calls off an open bet. It does not count for either side and is not offered again this week.</summary>
    Task<GameBetView?> CallOffBetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}
