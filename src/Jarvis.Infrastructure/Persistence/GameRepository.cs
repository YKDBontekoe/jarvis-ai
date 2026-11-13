using Jarvis.Application.Game;
using Jarvis.Domain.Game;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class GameBetEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string SubjectKey { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public string Claim { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int Points { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GameBet ToRecord() => new(Id, OwnerId, Kind, SubjectKey, SubjectId, Claim, Evidence, Confidence, Points,
        StartsOn, EndsOn, Status, ResolvedAt, CreatedAt, UpdatedAt);

    public static GameBetEntity From(GameBet bet) => new()
    {
        Id = bet.Id, OwnerId = bet.OwnerId, Kind = bet.Kind, SubjectKey = bet.SubjectKey, SubjectId = bet.SubjectId,
        Claim = bet.Claim, Evidence = bet.Evidence, Confidence = bet.Confidence, Points = bet.Points,
        StartsOn = bet.StartsOn, EndsOn = bet.EndsOn, Status = bet.Status, ResolvedAt = bet.ResolvedAt,
        CreatedAt = bet.CreatedAt, UpdatedAt = bet.UpdatedAt
    };
}

public sealed class GameQuestEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly WeekStart { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string SubjectKey { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int Target { get; set; }
    public int Points { get; set; }
    public bool IsBoss { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GameQuest ToRecord() => new(Id, OwnerId, WeekStart, Kind, SubjectKey, SubjectId, Title, Detail, Target,
        Points, IsBoss, Status, CompletedAt, CreatedAt, UpdatedAt);

    public static GameQuestEntity From(GameQuest quest) => new()
    {
        Id = quest.Id, OwnerId = quest.OwnerId, WeekStart = quest.WeekStart, Kind = quest.Kind,
        SubjectKey = quest.SubjectKey, SubjectId = quest.SubjectId, Title = quest.Title, Detail = quest.Detail,
        Target = quest.Target, Points = quest.Points, IsBoss = quest.IsBoss, Status = quest.Status,
        CompletedAt = quest.CompletedAt, CreatedAt = quest.CreatedAt, UpdatedAt = quest.UpdatedAt
    };
}

/// <summary>
/// Bets and quests, always filtered by owner. Unique indexes stop two refreshes running at once from placing the same
/// bet or writing the week's quests twice; the one that loses the race simply adds nothing.
/// </summary>
public sealed class GameRepository(JarvisDbContext db) : IGameRepository
{
    public async Task<IReadOnlyList<GameBet>> ListBetsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.GameBets.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<GameBet?> GetBetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.GameBets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task AddBetsAsync(IReadOnlyList<GameBet> bets, CancellationToken cancellationToken)
    {
        db.GameBets.AddRange(bets.Select(GameBetEntity.From));
        await SaveIgnoringDuplicatesAsync(cancellationToken);
    }

    public async Task<bool> UpdateBetAsync(GameBet bet, CancellationToken cancellationToken) =>
        await db.GameBets.Where(x => x.Id == bet.Id && x.OwnerId == bet.OwnerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, bet.Status)
                .SetProperty(x => x.ResolvedAt, bet.ResolvedAt)
                .SetProperty(x => x.UpdatedAt, bet.UpdatedAt), cancellationToken) > 0;

    public async Task<IReadOnlyList<GameQuest>> ListQuestsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.GameQuests.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<GameQuest?> GetQuestAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.GameQuests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task AddQuestsAsync(IReadOnlyList<GameQuest> quests, CancellationToken cancellationToken)
    {
        db.GameQuests.AddRange(quests.Select(GameQuestEntity.From));
        await SaveIgnoringDuplicatesAsync(cancellationToken);
    }

    public async Task<bool> UpdateQuestAsync(GameQuest quest, CancellationToken cancellationToken) =>
        await db.GameQuests.Where(x => x.Id == quest.Id && x.OwnerId == quest.OwnerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, quest.Status)
                .SetProperty(x => x.CompletedAt, quest.CompletedAt)
                .SetProperty(x => x.UpdatedAt, quest.UpdatedAt), cancellationToken) > 0;

    private async Task SaveIgnoringDuplicatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException
                                                   {
                                                       SqlState: Npgsql.PostgresErrorCodes.UniqueViolation
                                                   })
        {
            db.ChangeTracker.Clear();
        }
    }
}
