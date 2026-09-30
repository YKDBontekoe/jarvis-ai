using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MemoryIndexRepository(JarvisDbContext db) : IMemoryIndexRepository
{
    public async Task<IReadOnlyList<Guid>> ListOwnersNeedingIndexAsync(int limit, CancellationToken cancellationToken) =>
        await db.Memories.AsNoTracking()
            .Where(x => (x.ValidUntil == null || x.ValidUntil > DateTimeOffset.UtcNow) &&
                        (x.GraphIndexedAt == null || x.Embedding == null))
            .Select(x => x.OwnerId).Distinct().Take(limit).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemoryRecord>> ListNeedingEmbeddingAsync(Guid ownerId, string model, int limit,
        CancellationToken cancellationToken) =>
        (await Active(ownerId).Where(x => x.Embedding == null || x.EmbeddingModel != model)
            .OrderByDescending(x => x.UpdatedAt).Take(limit).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task<IReadOnlyList<MemoryRecord>> ListNeedingGraphIndexAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken) =>
        (await Active(ownerId).Where(x => x.GraphIndexedAt == null)
            .OrderBy(x => x.CreatedAt).Take(limit).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public Task SetEmbeddingAsync(Guid memoryId, Guid ownerId, MemoryEmbedding embedding,
        CancellationToken cancellationToken)
    {
        var vector = new Vector(embedding.Vector);
        return db.Memories.Where(x => x.Id == memoryId && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Embedding, vector)
                .SetProperty(x => x.EmbeddingModel, embedding.Model), cancellationToken);
    }

    public Task MarkGraphIndexedAsync(Guid memoryId, Guid ownerId, CancellationToken cancellationToken) =>
        db.Memories.Where(x => x.Id == memoryId && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.GraphIndexedAt, DateTimeOffset.UtcNow),
                cancellationToken);

    public async Task<MemoryIndexStatus> GetStatusAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var active = Active(ownerId);
        var model = await active.Where(x => x.EmbeddingModel != null).OrderByDescending(x => x.UpdatedAt)
            .Select(x => x.EmbeddingModel).FirstOrDefaultAsync(cancellationToken);
        return new MemoryIndexStatus(await active.CountAsync(cancellationToken),
            await active.CountAsync(x => x.Embedding != null, cancellationToken),
            await active.CountAsync(x => x.GraphIndexedAt != null, cancellationToken), model);
    }

    public async Task<IReadOnlyList<MemorySearchHit>> SearchSemanticAsync(Guid ownerId, MemoryEmbedding query,
        string? kind, double minimumSimilarity, int limit, CancellationToken cancellationToken)
    {
        var vector = new Vector(query.Vector);
        var hits = await Active(ownerId)
            .Where(x => x.Embedding != null && x.EmbeddingModel == query.Model && (kind == null || x.Kind == kind))
            .Select(x => new { Memory = x, Distance = x.Embedding!.CosineDistance(vector) })
            .OrderBy(x => x.Distance)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return hits.Where(hit => 1 - hit.Distance >= minimumSimilarity)
            .Select(hit => new MemorySearchHit(hit.Memory.ToRecord(), 1 - hit.Distance)).ToArray();
    }

    private IQueryable<MemoryEntity> Active(Guid ownerId) =>
        db.Memories.AsNoTracking().Where(x => x.OwnerId == ownerId &&
                                              (x.ValidUntil == null || x.ValidUntil > DateTimeOffset.UtcNow));
}
