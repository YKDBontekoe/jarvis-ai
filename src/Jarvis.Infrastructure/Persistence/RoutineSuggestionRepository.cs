using Jarvis.Application.Routines;
using Jarvis.Domain.Routines;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class RoutineSuggestionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public string Status { get; set; } = RoutineStatuses.Pending;
    public Guid? AutomationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public RoutineSuggestionEntry ToRecord() => new(Id, OwnerId, Fingerprint, Title, Evidence, Confidence,
        DefinitionJson, Status, AutomationId, CreatedAt, UpdatedAt);

    public void Apply(RoutineSuggestionEntry entry)
    {
        Fingerprint = entry.Fingerprint;
        Title = entry.Title;
        Evidence = entry.Evidence;
        Confidence = entry.Confidence;
        DefinitionJson = entry.DefinitionJson;
        Status = entry.Status;
        AutomationId = entry.AutomationId;
        UpdatedAt = entry.UpdatedAt;
    }
}

public sealed class RoutineSuggestionRepository(JarvisDbContext db) : IRoutineSuggestionRepository
{
    public async Task<IReadOnlyList<RoutineSuggestionEntry>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.RoutineSuggestions.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<RoutineSuggestionEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.RoutineSuggestions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddRangeAsync(IReadOnlyList<RoutineSuggestionEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            var entity = new RoutineSuggestionEntity
            {
                Id = entry.Id, OwnerId = entry.OwnerId, CreatedAt = entry.CreatedAt
            };
            entity.Apply(entry);
            db.RoutineSuggestions.Add(entity);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(RoutineSuggestionEntry entry, CancellationToken cancellationToken)
    {
        var entity = await db.RoutineSuggestions
            .SingleOrDefaultAsync(x => x.Id == entry.Id && x.OwnerId == entry.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(entry);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> DeleteManyAsync(Guid ownerId, IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await db.RoutineSuggestions.Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
}
