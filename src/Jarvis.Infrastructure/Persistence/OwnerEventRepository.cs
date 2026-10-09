using System.Text.Json;
using Jarvis.Application.Events;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class OwnerEventEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string? DataJson { get; set; }
    public Guid? ConversationId { get; set; }
    public string Origin { get; set; } = nameof(EventOrigin.System);
    public Guid? CausedByTaskId { get; set; }
    public DateTimeOffset At { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public OwnerEventRecord ToRecord() => new(Id, OwnerId, Kind, Summary,
        SubjectType is not null && SubjectId is { } subjectId ? new EntityRef(SubjectType, subjectId).ToString() : null,
        DataJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson, Json),
        ConversationId, Enum.TryParse<EventOrigin>(Origin, out var origin) ? origin : EventOrigin.System,
        CausedByTaskId, At);

    public static OwnerEventEntity From(JarvisEvent ev) => new()
    {
        Id = ev.Id ?? Guid.CreateVersion7(),
        OwnerId = ev.OwnerId,
        Kind = ev.Kind,
        Summary = ev.Summary,
        SubjectType = ev.Subject?.Type,
        SubjectId = ev.Subject?.Id,
        DataJson = ev.Data is { Count: > 0 } data ? JsonSerializer.Serialize(data, Json) : null,
        ConversationId = ev.ConversationId,
        Origin = ev.Origin.ToString(),
        CausedByTaskId = ev.CausedByTaskId,
        At = ev.At ?? DateTimeOffset.UtcNow
    };
}

public sealed class OwnerEventRepository(JarvisDbContext db) : IOwnerEventRepository
{
    public async Task AddAsync(JarvisEvent ev, CancellationToken cancellationToken)
    {
        var entity = OwnerEventEntity.From(ev);
        // A fresh insert outside any tracked work the caller may still be doing.
        await db.OwnerEvents.AddAsync(entity, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<OwnerEventRecord>> ListAsync(Guid ownerId, OwnerEventQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, JarvisEventLimits.MaxListLimit);
        var events = db.OwnerEvents.AsNoTracking().Where(x => x.OwnerId == ownerId);
        if (query.Since is { } since) events = events.Where(x => x.At > since);
        if (query.Kinds is { Count: > 0 } kinds) events = events.Where(x => kinds.Contains(x.Kind));
        if (query.Origins is { Count: > 0 } origins)
        {
            var names = origins.Select(origin => origin.ToString()).ToArray();
            events = events.Where(x => names.Contains(x.Origin));
        }
        if (EntityRef.TryParse(query.SubjectRef, out var subject))
            events = events.Where(x => x.SubjectType == subject.Type && x.SubjectId == subject.Id);
        return (await events.OrderByDescending(x => x.At).Take(limit).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToArray();
    }

    public Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        db.OwnerEvents.Where(x => x.At < olderThan).ExecuteDeleteAsync(cancellationToken);
}

public sealed class EntityLinkEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string FromType { get; set; } = string.Empty;
    public Guid FromId { get; set; }
    public string ToType { get; set; } = string.Empty;
    public Guid ToId { get; set; }
    public string Relation { get; set; } = LinkRelations.Related;
    public DateTimeOffset CreatedAt { get; set; }

    public EntityLinkRecord ToRecord() => new(Id, OwnerId, new EntityRef(FromType, FromId), new EntityRef(ToType, ToId),
        Relation, CreatedAt);
}

public sealed class EntityLinkRepository(JarvisDbContext db, TimeProvider? timeProvider = null) : IEntityLinkRepository
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<bool> AddAsync(Guid ownerId, EntityRef from, EntityRef to, string relation,
        CancellationToken cancellationToken)
    {
        if (from == to) return false;
        relation = LinkRelations.Normalize(relation);
        if (await db.EntityLinks.AnyAsync(x => x.OwnerId == ownerId && x.FromType == from.Type && x.FromId == from.Id &&
                x.ToType == to.Type && x.ToId == to.Id && x.Relation == relation, cancellationToken))
            return false;
        var entity = new EntityLinkEntity
        {
            Id = Guid.CreateVersion7(), OwnerId = ownerId, FromType = from.Type, FromId = from.Id, ToType = to.Type,
            ToId = to.Id, Relation = relation, CreatedAt = _clock.GetUtcNow()
        };
        db.EntityLinks.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two writers raced on the unique index; the link exists either way.
            return false;
        }
        finally
        {
            db.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<EntityLinkRecord>> ListForAsync(Guid ownerId, EntityRef entity, int limit,
        CancellationToken cancellationToken) =>
        (await db.EntityLinks.AsNoTracking()
            .Where(x => x.OwnerId == ownerId &&
                        ((x.FromType == entity.Type && x.FromId == entity.Id) ||
                         (x.ToType == entity.Type && x.ToId == entity.Id)))
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<bool> RemoveAsync(Guid ownerId, EntityRef from, EntityRef to, string relation,
        CancellationToken cancellationToken)
    {
        relation = LinkRelations.Normalize(relation);
        return await db.EntityLinks.Where(x => x.OwnerId == ownerId && x.FromType == from.Type && x.FromId == from.Id &&
                                              x.ToType == to.Type && x.ToId == to.Id && x.Relation == relation)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
