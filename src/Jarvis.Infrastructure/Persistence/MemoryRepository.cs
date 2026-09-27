using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MemoryRepository(JarvisDbContext db) : IMemoryRepository
{
    public Task<bool> HasActiveMemoriesAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.Memories.AnyAsync(x => x.OwnerId == ownerId && (x.ValidUntil == null || x.ValidUntil > DateTimeOffset.UtcNow), cancellationToken);

    public async Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance,
        float confidence, string? sourceType, Guid? sourceId, DateTimeOffset? validUntil, bool isPinned,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var memory = new MemoryEntity
        {
            Id = Guid.CreateVersion7(), OwnerId = ownerId, Kind = kind, Content = content,
            Importance = importance, Confidence = confidence, Embedding = null, EmbeddingModel = null, GraphIndexedAt = null,
            SourceType = sourceType, SourceId = sourceId, CreatedAt = now, UpdatedAt = now,
            ValidUntil = validUntil, IsPinned = isPinned
        };
        db.Memories.Add(memory);
        await db.SaveChangesAsync(cancellationToken);
        return memory.ToRecord();
    }

    public async Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
        float importance, float confidence, string? sourceType, Guid? sourceId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Memories.SingleOrDefaultAsync(x => x.Id == existingId && x.OwnerId == ownerId &&
            (x.ValidUntil == null || x.ValidUntil > DateTimeOffset.UtcNow), cancellationToken);
        if (existing is null || existing.Kind != kind || existing.IsPinned) return null;

        var now = DateTimeOffset.UtcNow;
        existing.ValidUntil = now;
        existing.UpdatedAt = now;
        var replacement = new MemoryEntity
        {
            Id = Guid.CreateVersion7(), OwnerId = ownerId, Kind = kind, Content = content,
            Importance = importance, Confidence = confidence, Embedding = null, EmbeddingModel = null, GraphIndexedAt = null,
            SourceType = sourceType, SourceId = sourceId, CreatedAt = now, UpdatedAt = now,
            ValidUntil = null, IsPinned = false
        };
        db.Memories.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await db.GraphRelations
            .Where(x => x.OwnerId == ownerId && x.SourceMemoryId == existing.Id && x.ValidTo == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ValidTo, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement.ToRecord();
    }

    public async Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Memories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken) =>
        (await db.Memories.AsNoTracking().Where(x => x.OwnerId == ownerId && (kind == null || x.Kind == kind))
            .OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.UpdatedAt)
            .Take(500).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Memories.AsNoTracking().Where(x => x.OwnerId == ownerId && x.IsPinned &&
                (x.ValidUntil == null || x.ValidUntil > DateTimeOffset.UtcNow))
            .OrderByDescending(x => x.Importance).ThenByDescending(x => x.UpdatedAt)
            .Take(8).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
        float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken)
    {
        var memory = await db.Memories.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (memory is null) return null;
        memory.Kind = kind;
        memory.Content = content;
        memory.Importance = importance;
        memory.Confidence = confidence;
        memory.Embedding = null;
        memory.EmbeddingModel = null;
        memory.GraphIndexedAt = null;
        memory.ValidUntil = validUntil;
        memory.IsPinned = isPinned;
        memory.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return memory.ToRecord();
    }

    public async Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var memory = await db.Memories.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (memory is null) return;
        db.Memories.Remove(memory);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryRecord>> SearchTextAsync(Guid ownerId, string query, string? kind,
        CancellationToken cancellationToken) =>
        (await db.Memories.FromSqlInterpolated($"""
            SELECT * FROM memories
            WHERE owner_id = {ownerId}
              AND (CAST({kind} AS text) IS NULL OR kind = CAST({kind} AS text))
              AND (valid_until IS NULL OR valid_until > now())
              AND search_vector @@ websearch_to_tsquery('simple', {query})
            ORDER BY ts_rank(search_vector, websearch_to_tsquery('simple', {query})) DESC
            LIMIT 20
            """).AsNoTracking().ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<MemoryRecord>> SearchTrigramAsync(Guid ownerId, string query, string? kind,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL pg_trgm.similarity_threshold = 0.15", cancellationToken);
        var matches = await db.Memories.FromSqlInterpolated($"""
            SELECT * FROM memories
            WHERE owner_id = {ownerId}
              AND (CAST({kind} AS text) IS NULL OR kind = CAST({kind} AS text))
              AND (valid_until IS NULL OR valid_until > now())
              AND content % {query}
            ORDER BY similarity(content, {query}) DESC
            LIMIT 20
            """).AsNoTracking().ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return matches.Select(x => x.ToRecord()).ToList();
    }

}
