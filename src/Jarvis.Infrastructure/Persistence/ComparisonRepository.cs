using System.Text.Json;
using Jarvis.Application.Comparisons;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ComparisonEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public Guid? LibraryItemId { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ComparisonRepository(JarvisDbContext db) : IComparisonRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ComparisonRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Comparisons.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Take(IComparisonService.MaxPerOwner)
            .ToListAsync(cancellationToken)).Select(ToRecord).ToArray();

    public async Task<ComparisonRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Comparisons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken) is { } entity ? ToRecord(entity) : null;

    public Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.Comparisons.CountAsync(x => x.OwnerId == ownerId, cancellationToken);

    public async Task AddAsync(ComparisonRecord record, CancellationToken cancellationToken)
    {
        db.Comparisons.Add(Apply(new ComparisonEntity { Id = record.Id, OwnerId = record.OwnerId, CreatedAt = record.CreatedAt }, record));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(ComparisonRecord record, CancellationToken cancellationToken)
    {
        var entity = await db.Comparisons.SingleOrDefaultAsync(x => x.Id == record.Id && x.OwnerId == record.OwnerId,
            cancellationToken);
        if (entity is null) return false;
        Apply(entity, record);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Comparisons.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    private static ComparisonEntity Apply(ComparisonEntity entity, ComparisonRecord record)
    {
        entity.Title = record.Definition.Title;
        entity.ProjectId = record.ProjectId;
        entity.LibraryItemId = record.LibraryItemId;
        entity.DefinitionJson = JsonSerializer.Serialize(record.Definition, Json);
        entity.UpdatedAt = record.UpdatedAt;
        return entity;
    }

    private static ComparisonRecord ToRecord(ComparisonEntity entity) =>
        new(entity.Id, entity.OwnerId,
            JsonSerializer.Deserialize<ComparisonDefinition>(entity.DefinitionJson, Json)!, entity.ProjectId,
            entity.LibraryItemId, entity.CreatedAt, entity.UpdatedAt);
}
