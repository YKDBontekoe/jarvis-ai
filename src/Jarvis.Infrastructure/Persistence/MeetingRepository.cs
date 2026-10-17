using System.Text.Json;
using Jarvis.Application.Meetings;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MeetingEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? StartsAt { get; set; }
    public Guid? ProjectId { get; set; }
    public string DocumentJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MeetingRepository(JarvisDbContext db) : IMeetingRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MeetingRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Meetings.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.StartsAt ?? x.UpdatedAt).ThenBy(x => x.Id).Take(MeetingRules.MaxPerOwner)
            .ToListAsync(cancellationToken)).Select(ToRecord).ToArray();

    public async Task<MeetingRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Meetings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken) is { } entity ? ToRecord(entity) : null;

    public Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.Meetings.CountAsync(x => x.OwnerId == ownerId, cancellationToken);

    public async Task AddAsync(MeetingRecord record, CancellationToken cancellationToken)
    {
        db.Meetings.Add(Apply(new MeetingEntity { Id = record.Id, OwnerId = record.OwnerId, CreatedAt = record.CreatedAt }, record));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(MeetingRecord record, CancellationToken cancellationToken)
    {
        var entity = await db.Meetings.SingleOrDefaultAsync(x => x.Id == record.Id && x.OwnerId == record.OwnerId,
            cancellationToken);
        if (entity is null) return false;
        Apply(entity, record);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Meetings.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    private static MeetingEntity Apply(MeetingEntity entity, MeetingRecord record)
    {
        entity.Title = record.Title;
        entity.StartsAt = record.StartsAt;
        entity.ProjectId = record.ProjectId;
        entity.DocumentJson = JsonSerializer.Serialize(record.Document, Json);
        entity.UpdatedAt = record.UpdatedAt;
        return entity;
    }

    private static MeetingRecord ToRecord(MeetingEntity entity) =>
        new(entity.Id, entity.OwnerId, entity.Title, entity.StartsAt, entity.ProjectId,
            JsonSerializer.Deserialize<MeetingDocument>(entity.DocumentJson, Json)!, entity.CreatedAt, entity.UpdatedAt);
}
