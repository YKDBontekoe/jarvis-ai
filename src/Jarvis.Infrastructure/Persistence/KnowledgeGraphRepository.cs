using System.Text.Json;
using Jarvis.Application.Memory;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class GraphEntityEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = "thing";
    public string? Summary { get; set; }
    public string AliasesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GraphRelationEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid SubjectId { get; set; }
    public string Predicate { get; set; } = string.Empty;
    public Guid? ObjectId { get; set; }
    public string? ObjectValue { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public float Confidence { get; set; }
    public Guid? SourceMemoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class KnowledgeGraphRepository(JarvisDbContext db) : IKnowledgeGraphRepository
{
    private const int MaxEntitiesPerOwner = 2_000;

    public async Task<IReadOnlyList<GraphEntityRecord>> ListEntitiesAsync(Guid ownerId, string? search, int limit,
        CancellationToken cancellationToken)
    {
        var query = db.GraphEntities.AsNoTracking().Where(x => x.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("%", "").Replace("_", "") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.AliasesJson, pattern));
        }
        var entities = await query.OrderByDescending(x => x.UpdatedAt).Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return await ToRecordsAsync(ownerId, entities, cancellationToken);
    }

    public async Task<GraphEntityDetails?> GetEntityAsync(Guid ownerId, Guid entityId,
        CancellationToken cancellationToken)
    {
        var entity = await db.GraphEntities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == entityId, cancellationToken);
        return entity is null ? null : await DetailsAsync(ownerId, entity, null, cancellationToken);
    }

    public async Task<GraphEntityDetails?> FindEntityAsync(Guid ownerId, string name, DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var key = GraphNames.Key(name);
        var entity = await db.GraphEntities.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Key == key, cancellationToken)
                     ?? await db.GraphEntities.AsNoTracking()
                         .Where(x => x.OwnerId == ownerId &&
                                     (EF.Functions.ILike(x.Name, name.Trim()) ||
                                      EF.Functions.ILike(x.AliasesJson, "%\"" + name.Trim() + "\"%")))
                         .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : await DetailsAsync(ownerId, entity, asOf, cancellationToken);
    }

    public async Task<IReadOnlyList<GraphEntityRecord>> FindMentionedAsync(Guid ownerId, string text, int limit,
        CancellationToken cancellationToken)
    {
        var lower = text.ToLowerInvariant();
        var candidates = await db.GraphEntities.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.Key != GraphNames.UserKey && x.Name.Length >= 3)
            .OrderByDescending(x => x.UpdatedAt).Take(500).ToListAsync(cancellationToken);
        var mentioned = candidates.Where(entity =>
                ContainsWord(lower, entity.Name.ToLowerInvariant()) ||
                ParseAliases(entity.AliasesJson).Any(alias => alias.Length >= 3 && ContainsWord(lower, alias.ToLowerInvariant())))
            .Take(limit).ToList();
        return await ToRecordsAsync(ownerId, mentioned, cancellationToken);
    }

    public async Task<GraphOverview> GetOverviewAsync(Guid ownerId, int limit, CancellationToken cancellationToken)
    {
        var current = db.GraphRelations.AsNoTracking().Where(x => x.OwnerId == ownerId && x.ValidTo == null);
        var degrees = await current.Select(x => x.SubjectId)
            .Concat(current.Where(x => x.ObjectId != null).Select(x => x.ObjectId!.Value))
            .GroupBy(id => id).Select(group => new { Id = group.Key, Count = group.Count() })
            .OrderByDescending(x => x.Count).Take(Math.Clamp(limit, 1, 150)).ToListAsync(cancellationToken);
        var ids = degrees.Select(x => x.Id).ToHashSet();
        var entities = await db.GraphEntities.AsNoTracking().Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var edgeRows = await current
            .Where(x => x.ObjectId != null && ids.Contains(x.SubjectId) && ids.Contains(x.ObjectId!.Value))
            .OrderByDescending(x => x.Confidence)
            .Select(x => new { x.SubjectId, ObjectId = x.ObjectId!.Value, x.Predicate, x.Confidence, x.ValidFrom })
            .Take(400).ToListAsync(cancellationToken);
        var literalRows = await current
            .Where(x => x.ObjectId == null && !string.IsNullOrEmpty(x.ObjectValue) && ids.Contains(x.SubjectId))
            .OrderByDescending(x => x.Confidence)
            .Select(x => new { x.SubjectId, x.Predicate, x.ObjectValue, x.Confidence, x.ValidFrom })
            .Take(400).ToListAsync(cancellationToken);
        var edges = edgeRows.Select(x => new GraphEdge(x.SubjectId, x.ObjectId, x.Predicate, x.Confidence, x.ValidFrom))
            .ToArray();
        var literals = literalRows
            .Select(x => new GraphLiteral(x.SubjectId, x.Predicate, x.ObjectValue!, x.Confidence, x.ValidFrom))
            .ToArray();
        return new GraphOverview(await ToRecordsAsync(ownerId, entities, cancellationToken), edges, literals);
    }

    public async Task<int> MergeAsync(Guid ownerId, IReadOnlyList<GraphFact> facts, Guid? sourceMemoryId,
        CancellationToken cancellationToken)
    {
        if (facts.Count == 0) return 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        var cache = new Dictionary<string, GraphEntityEntity>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            var predicate = GraphNames.Predicate(fact.Predicate);
            if (predicate.Length == 0) continue;
            var subject = await UpsertEntityAsync(ownerId, fact.Subject, fact.SubjectType, cache, now, cancellationToken);
            GraphEntityEntity? objectEntity = null;
            string? objectValue = null;
            if (fact.ObjectIsEntity)
                objectEntity = await UpsertEntityAsync(ownerId, fact.Object, fact.ObjectType ?? "thing", cache, now,
                    cancellationToken);
            else
                objectValue = fact.Object.Trim() is { Length: > 0 } value ? value[..Math.Min(value.Length, 300)] : null;
            if (subject is null || (objectEntity is null && objectValue is null) || objectEntity?.Id == subject.Id)
                continue;

            var currentValues = await db.GraphRelations
                .Where(x => x.OwnerId == ownerId && x.SubjectId == subject.Id && x.Predicate == predicate &&
                            x.ValidTo == null)
                .ToListAsync(cancellationToken);
            var same = currentValues.FirstOrDefault(x => objectEntity is not null
                ? x.ObjectId == objectEntity.Id
                : x.ObjectId == null && string.Equals(x.ObjectValue, objectValue, StringComparison.OrdinalIgnoreCase));
            if (same is not null)
            {
                same.Confidence = Math.Min(1f, Math.Max(same.Confidence, fact.Confidence) + 0.05f);
                continue;
            }
            if (fact.Exclusive)
                foreach (var previous in currentValues)
                    previous.ValidTo = fact.ValidFrom > previous.ValidFrom ? fact.ValidFrom : now;

            db.GraphRelations.Add(new GraphRelationEntity
            {
                Id = Guid.CreateVersion7(), OwnerId = ownerId, SubjectId = subject.Id, Predicate = predicate,
                ObjectId = objectEntity?.Id, ObjectValue = objectValue, ValidFrom = fact.ValidFrom,
                Confidence = Math.Clamp(fact.Confidence, 0f, 1f), SourceMemoryId = sourceMemoryId, CreatedAt = now
            });
            subject.UpdatedAt = now;
            changed++;
            await db.SaveChangesAsync(cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task<GraphEntityRecord?> UpdateEntityAsync(Guid ownerId, Guid entityId, string? name, string? type,
        string? summary, CancellationToken cancellationToken)
    {
        var entity = await db.GraphEntities.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == entityId,
            cancellationToken);
        if (entity is null) return null;
        if (!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120 && entity.Key != GraphNames.UserKey)
        {
            var trimmed = name.Trim();
            if (!string.Equals(entity.Name, trimmed, StringComparison.Ordinal))
            {
                var aliases = ParseAliases(entity.AliasesJson);
                if (!aliases.Contains(entity.Name, StringComparer.OrdinalIgnoreCase) && aliases.Count < 10)
                    aliases.Add(entity.Name);
                entity.AliasesJson = JsonSerializer.Serialize(aliases);
                entity.Name = trimmed;
            }
        }
        if (!string.IsNullOrWhiteSpace(type)) entity.Type = GraphEntityTypes.Normalize(type);
        if (summary is not null) entity.Summary = summary.Trim() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, 500)]
            : null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (await ToRecordsAsync(ownerId, [entity], cancellationToken))[0];
    }

    public async Task<bool> CloseRelationAsync(Guid ownerId, Guid relationId, CancellationToken cancellationToken)
    {
        var relation = await db.GraphRelations.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == relationId,
            cancellationToken);
        if (relation is null || relation.ValidTo is not null) return false;
        relation.ValidTo = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MergeEntitiesAsync(Guid ownerId, Guid keepId, Guid absorbId,
        CancellationToken cancellationToken)
    {
        if (keepId == absorbId) throw new ArgumentException("Choose two different entities to merge.");
        var keep = await db.GraphEntities.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == keepId,
            cancellationToken);
        var absorb = await db.GraphEntities.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == absorbId,
            cancellationToken);
        if (keep is null || absorb is null) return false;
        var now = DateTimeOffset.UtcNow;
        var aliases = ParseAliases(keep.AliasesJson);
        if (!aliases.Contains(absorb.Name, StringComparer.OrdinalIgnoreCase) && aliases.Count < 10)
            aliases.Add(absorb.Name);
        keep.AliasesJson = JsonSerializer.Serialize(aliases);
        keep.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(keep.Summary) && !string.IsNullOrWhiteSpace(absorb.Summary))
            keep.Summary = absorb.Summary;
        await db.GraphRelations.Where(x => x.OwnerId == ownerId && x.SubjectId == absorbId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.SubjectId, keepId), cancellationToken);
        await db.GraphRelations.Where(x => x.OwnerId == ownerId && x.ObjectId == absorbId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ObjectId, keepId), cancellationToken);
        db.GraphEntities.Remove(absorb);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken) =>
        await db.GraphEntities.Where(x => x.OwnerId == ownerId && x.Id == entityId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private async Task<GraphEntityEntity?> UpsertEntityAsync(Guid ownerId, string name, string type,
        Dictionary<string, GraphEntityEntity> cache, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var key = GraphNames.Key(trimmed);
        if (key.Length == 0 || trimmed.Length > 120) return null;
        if (cache.TryGetValue(key, out var cached)) return cached;
        var entity = await db.GraphEntities.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Key == key,
            cancellationToken);
        if (entity is null)
        {
            if (await db.GraphEntities.CountAsync(x => x.OwnerId == ownerId, cancellationToken) >= MaxEntitiesPerOwner)
                return null;
            entity = new GraphEntityEntity
            {
                Id = Guid.CreateVersion7(), OwnerId = ownerId, Key = key,
                Name = key == GraphNames.UserKey ? "You" : trimmed,
                Type = key == GraphNames.UserKey ? "person" : GraphEntityTypes.Normalize(type),
                CreatedAt = now, UpdatedAt = now
            };
            db.GraphEntities.Add(entity);
        }
        else if (!string.Equals(entity.Name, trimmed, StringComparison.Ordinal) && key != GraphNames.UserKey)
        {
            var aliases = ParseAliases(entity.AliasesJson);
            if (!aliases.Contains(trimmed, StringComparer.OrdinalIgnoreCase) && aliases.Count < 10)
                entity.AliasesJson = JsonSerializer.Serialize(aliases.Append(trimmed));
        }
        cache[key] = entity;
        return entity;
    }

    private async Task<GraphEntityDetails> DetailsAsync(Guid ownerId, GraphEntityEntity entity, DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var relations = await db.GraphRelations.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && (x.SubjectId == entity.Id || x.ObjectId == entity.Id))
            .OrderByDescending(x => x.ValidFrom).Take(200).ToListAsync(cancellationToken);
        var ids = relations.SelectMany(x => new[] { x.SubjectId, x.ObjectId ?? Guid.Empty }).Distinct().ToArray();
        var names = await db.GraphEntities.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var records = relations.Select(x => new GraphRelationRecord(x.Id, x.SubjectId,
            names.GetValueOrDefault(x.SubjectId, "?"), x.Predicate, x.ObjectId,
            x.ObjectId is { } objectId ? names.GetValueOrDefault(objectId) : null, x.ObjectValue, x.ValidFrom,
            x.ValidTo, x.Confidence, x.SourceMemoryId)).ToArray();
        bool IsCurrent(GraphRelationRecord relation) => asOf is { } at
            ? relation.ValidFrom <= at && (relation.ValidTo is null || relation.ValidTo > at)
            : relation.ValidTo is null;
        var entityRecord = (await ToRecordsAsync(ownerId, [entity], cancellationToken))[0];
        return new GraphEntityDetails(entityRecord, records.Where(IsCurrent).ToArray(),
            records.Where(relation => !IsCurrent(relation)).ToArray());
    }

    private async Task<IReadOnlyList<GraphEntityRecord>> ToRecordsAsync(Guid ownerId,
        IReadOnlyList<GraphEntityEntity> entities, CancellationToken cancellationToken)
    {
        var ids = entities.Select(x => x.Id).ToArray();
        var counts = await db.GraphRelations.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ValidTo == null &&
                        (ids.Contains(x.SubjectId) || (x.ObjectId != null && ids.Contains(x.ObjectId.Value))))
            .Select(x => new { x.SubjectId, x.ObjectId })
            .ToListAsync(cancellationToken);
        return entities.Select(entity => new GraphEntityRecord(entity.Id, entity.Name, entity.Type, entity.Summary,
            ParseAliases(entity.AliasesJson),
            counts.Count(x => x.SubjectId == entity.Id || x.ObjectId == entity.Id), entity.UpdatedAt)).ToArray();
    }

    private static List<string> ParseAliases(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool ContainsWord(string haystack, string needle)
    {
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var after = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (before && after) return true;
            index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }
        return false;
    }
}
