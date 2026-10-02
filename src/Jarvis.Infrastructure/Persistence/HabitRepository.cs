using Jarvis.Application.Habits;
using Jarvis.Domain.Habits;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jarvis.Infrastructure.Persistence;

public sealed class HabitEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string Cadence { get; set; } = HabitCadences.Daily;
    public int TargetPerWeek { get; set; } = 1;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<HabitCheckInEntity> CheckIns { get; set; } = [];

    public Habit ToRecord() =>
        new(Id, OwnerId, Name, Icon, Cadence, TargetPerWeek, ArchivedAt, CreatedAt, UpdatedAt);
}

public sealed class HabitCheckInEntity
{
    public Guid Id { get; set; }
    public Guid HabitId { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly Date { get; set; }
    public string Source { get; set; } = HabitSources.App;
    public DateTimeOffset CreatedAt { get; set; }

    public HabitCheckIn ToRecord() => new(Id, HabitId, OwnerId, Date, Source, CreatedAt);
}

public sealed class HabitRepository(JarvisDbContext db) : IHabitRepository
{
    public async Task<IReadOnlyList<Habit>> ListAsync(Guid ownerId, bool includeArchived,
        CancellationToken cancellationToken) =>
        (await db.Habits.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && (includeArchived || x.ArchivedAt == null))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Habit?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Habits.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddAsync(Habit habit, CancellationToken cancellationToken)
    {
        db.Habits.Add(new HabitEntity
        {
            Id = habit.Id, OwnerId = habit.OwnerId, Name = habit.Name, Icon = habit.Icon, Cadence = habit.Cadence,
            TargetPerWeek = habit.TargetPerWeek, ArchivedAt = habit.ArchivedAt, CreatedAt = habit.CreatedAt,
            UpdatedAt = habit.UpdatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Habit habit, CancellationToken cancellationToken) =>
        await db.Habits.Where(x => x.Id == habit.Id && x.OwnerId == habit.OwnerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Name, habit.Name)
                .SetProperty(x => x.Icon, habit.Icon)
                .SetProperty(x => x.Cadence, habit.Cadence)
                .SetProperty(x => x.TargetPerWeek, habit.TargetPerWeek)
                .SetProperty(x => x.ArchivedAt, habit.ArchivedAt)
                .SetProperty(x => x.UpdatedAt, habit.UpdatedAt), cancellationToken) > 0;

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Habits.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<HabitCheckIn>> ListCheckInsAsync(Guid ownerId,
        IReadOnlyCollection<Guid> habitIds, CancellationToken cancellationToken)
    {
        var ids = habitIds.ToArray();
        return (await db.HabitCheckIns.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && ids.Contains(x.HabitId))
                .OrderBy(x => x.Date)
                .ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToArray();
    }

    public async Task<bool> AddCheckInAsync(HabitCheckIn checkIn, CancellationToken cancellationToken)
    {
        if (!await db.Habits.AnyAsync(x => x.Id == checkIn.HabitId && x.OwnerId == checkIn.OwnerId,
                cancellationToken))
            return false;
        if (await db.HabitCheckIns.AnyAsync(x => x.HabitId == checkIn.HabitId && x.Date == checkIn.Date,
                cancellationToken))
            return false;
        var entity = new HabitCheckInEntity
        {
            Id = checkIn.Id, HabitId = checkIn.HabitId, OwnerId = checkIn.OwnerId, Date = checkIn.Date,
            Source = checkIn.Source, CreatedAt = checkIn.CreatedAt
        };
        db.HabitCheckIns.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.UniqueViolation
                                                  })
        {
            // Two taps (or the app and the chat) checked in the same day at once; the first one wins.
            db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> DeleteCheckInAsync(Guid ownerId, Guid habitId, DateOnly date,
        CancellationToken cancellationToken) =>
        await db.HabitCheckIns.Where(x => x.OwnerId == ownerId && x.HabitId == habitId && x.Date == date)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<Guid>> ListOwnersWithActiveHabitsAsync(CancellationToken cancellationToken) =>
        await db.Habits.AsNoTracking().Where(x => x.ArchivedAt == null).Select(x => x.OwnerId).Distinct()
            .ToListAsync(cancellationToken);
}
