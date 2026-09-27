using Jarvis.Application.Skills;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class SkillEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Source { get; set; } = SkillSources.User;
    public string Status { get; set; } = SkillStatuses.Active;
    public bool IsLocked { get; set; }
    public int Version { get; set; }
    public int UseCount { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SkillRecord ToRecord() => new(Id, OwnerId, Name, Description, Instructions, Source, Status, IsLocked,
        Version, UseCount, LastUsedAt, CreatedAt, UpdatedAt);
}

public sealed class SkillRevisionEntity
{
    public Guid Id { get; set; }
    public Guid SkillId { get; set; }
    public int Version { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Source { get; set; } = SkillSources.User;
    public string? ChangeNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SkillRepository(JarvisDbContext db) : ISkillRepository
{
    private const int MaxSkillsPerOwner = 200;

    public async Task<IReadOnlyList<SkillRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Skills.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Status).ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<SkillRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Skills.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task<SkillRecord?> FindByNameAsync(Guid ownerId, string name, CancellationToken cancellationToken) =>
        (await db.Skills.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Name == name,
            cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<SkillRevisionRecord>> ListRevisionsAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await db.Skills.AnyAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken)) return [];
        return (await db.SkillRevisions.AsNoTracking().Where(x => x.SkillId == id)
                .OrderByDescending(x => x.Version).Take(50).ToListAsync(cancellationToken))
            .Select(x => new SkillRevisionRecord(x.Version, x.Description, x.Instructions, x.Source, x.ChangeNote,
                x.CreatedAt))
            .ToArray();
    }

    public async Task<SkillRecord?> UpsertAsync(Guid ownerId, SkillDraft draft, string source, string statusForNew,
        bool respectLock, string? changeNote, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Skills.FromSqlInterpolated(
                $"SELECT * FROM skills WHERE owner_id = {ownerId} AND name = {draft.Name} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            if (await db.Skills.CountAsync(x => x.OwnerId == ownerId, cancellationToken) >= MaxSkillsPerOwner)
                throw new ArgumentException($"Keep at most {MaxSkillsPerOwner} skills; delete unused ones first.");
            existing = new SkillEntity
            {
                Id = Guid.CreateVersion7(), OwnerId = ownerId, Name = draft.Name, Source = source,
                Status = statusForNew, CreatedAt = now, IsLocked = source == SkillSources.User
            };
            db.Skills.Add(existing);
        }
        else if (respectLock && existing.IsLocked)
        {
            return null;
        }
        else if (existing.Description == draft.Description && existing.Instructions == draft.Instructions)
        {
            return existing.ToRecord();
        }

        existing.Description = draft.Description;
        existing.Instructions = draft.Instructions;
        existing.Version++;
        existing.UpdatedAt = now;
        if (source == SkillSources.User) existing.IsLocked = true;
        db.SkillRevisions.Add(new SkillRevisionEntity
        {
            Id = Guid.CreateVersion7(), SkillId = existing.Id, Version = existing.Version,
            Description = draft.Description, Instructions = draft.Instructions, Source = source,
            ChangeNote = changeNote is { Length: > 500 } ? changeNote[..500] : changeNote, CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return existing.ToRecord();
    }

    public async Task<SkillRecord?> SetStatusAsync(Guid id, Guid ownerId, string status,
        CancellationToken cancellationToken)
    {
        var skill = await db.Skills.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (skill is null) return null;
        skill.Status = status;
        skill.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return skill.ToRecord();
    }

    public async Task<SkillRecord?> SetLockedAsync(Guid id, Guid ownerId, bool locked,
        CancellationToken cancellationToken)
    {
        var skill = await db.Skills.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (skill is null) return null;
        skill.IsLocked = locked;
        skill.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return skill.ToRecord();
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Skills.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    public Task RecordUseAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        db.Skills.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.UseCount, x => x.UseCount + 1)
                .SetProperty(x => x.LastUsedAt, DateTimeOffset.UtcNow), cancellationToken);
}
