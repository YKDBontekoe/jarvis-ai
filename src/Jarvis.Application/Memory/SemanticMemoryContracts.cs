using Jarvis.Domain.Memory;

namespace Jarvis.Application.Memory;

public static class MemoryText
{
    /// <summary>What gets embedded and keyword-indexed: the memory plus its model-written search hints.</summary>
    public static string ForIndex(MemoryRecord memory) =>
        string.IsNullOrWhiteSpace(memory.SearchHints) ? memory.Content : memory.Content + " " + memory.SearchHints;
}

public sealed record MemoryEmbedding(string Model, float[] Vector);

public sealed record MemoryIndexStatus(int Active, int Embedded, int GraphIndexed, string? EmbeddingModel);

/// <summary>Creates embeddings with the owner's configured model; returns null when semantic search is off.</summary>
public interface IMemoryEmbedder
{
    Task<IReadOnlyList<MemoryEmbedding>?> EmbedAsync(Guid ownerId, IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}

/// <summary>Background indexing state for embeddings and the knowledge graph.</summary>
public interface IMemoryIndexRepository
{
    Task<IReadOnlyList<Guid>> ListOwnersNeedingIndexAsync(int limit, CancellationToken cancellationToken);
    /// <summary>Active memories that have no search hints yet, newest first.</summary>
    Task<IReadOnlyList<MemoryRecord>> ListNeedingHintsAsync(Guid ownerId, int limit, CancellationToken cancellationToken);
    /// <summary>Stores search hints (empty string = none worth adding) and queues the memory for re-embedding.</summary>
    Task SetSearchHintsAsync(Guid memoryId, Guid ownerId, string hints, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListNeedingEmbeddingAsync(Guid ownerId, string model, int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListNeedingGraphIndexAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken);
    Task SetEmbeddingAsync(Guid memoryId, Guid ownerId, MemoryEmbedding embedding, CancellationToken cancellationToken);
    Task MarkGraphIndexedAsync(Guid memoryId, Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryIndexStatus> GetStatusAsync(Guid ownerId, CancellationToken cancellationToken);
    /// <summary>Nearest memories by cosine similarity; each hit's score is the similarity.</summary>
    Task<IReadOnlyList<MemorySearchHit>> SearchSemanticAsync(Guid ownerId, MemoryEmbedding query, string? kind,
        double minimumSimilarity, int limit, CancellationToken cancellationToken);
}
