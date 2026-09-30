using System.Diagnostics;
using System.Diagnostics.Metrics;
using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;

namespace Jarvis.Memory;

public sealed class MemoryService(IMemoryRepository repository, IMemoryIndexRepository? index = null,
    IMemoryEmbedder? embedder = null) : IMemoryService
{
    internal const double MinimumSemanticSimilarity = 0.3;
    private const int CandidateLimit = 30;

    public async Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance,
        float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken,
        string sourceType = "user", Guid? sourceId = null, Guid? profileId = null)
    {
        Validate(kind, content, importance, confidence);
        if (sourceType is not ("user" or "conversation" or "journal") || sourceId == Guid.Empty)
            throw new ArgumentException("Memory source metadata is invalid.", nameof(sourceType));
        return await repository.CreateAsync(ownerId, kind, content.Trim(), importance, confidence,
            sourceType, sourceId, validUntil, isPinned, cancellationToken, profileId);
    }

    public async Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
        float importance, float confidence, CancellationToken cancellationToken,
        string sourceType = "conversation", Guid? sourceId = null)
    {
        Validate(kind, content, importance, confidence);
        if (sourceType is not ("user" or "conversation" or "journal") || sourceId == Guid.Empty)
            throw new ArgumentException("Memory source metadata is invalid.", nameof(sourceType));
        return await repository.ReplaceAsync(existingId, ownerId, kind, content.Trim(), importance, confidence,
            sourceType, sourceId, cancellationToken);
    }

    public Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetAsync(id, ownerId, cancellationToken);

    public Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken)
    {
        if (!MemoryKinds.IsValidFilter(kind)) throw new ArgumentException("Unknown memory kind.", nameof(kind));
        return repository.ListAsync(ownerId, kind, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        repository.ListPinnedAsync(ownerId, cancellationToken);

    public async Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
        float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken)
    {
        Validate(kind, content, importance, confidence);
        return await repository.UpdateAsync(id, ownerId, kind, content.Trim(), importance, confidence,
            MemoryValidity.ForUpdate(validUntil), isPinned, cancellationToken);
    }

    public Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
        CancellationToken cancellationToken, string? kind = null)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "completed";
        IReadOnlyList<MemorySearchHit> hits = [];
        using var activity = MemoryDiagnostics.ActivitySource.StartActivity("jarvis.memory.search");
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return hits;
            if (!MemoryKinds.IsValidFilter(kind))
                throw new ArgumentException("Unknown memory kind.", nameof(kind));
            // The existence check only guards the paid embedding call; keyword search on an empty store is free.
            var semanticEnabled = index is not null && embedder is not null &&
                                  await repository.HasActiveMemoriesAsync(ownerId, cancellationToken);

            // The embedding request goes over the network while the keyword query runs in PostgreSQL.
            var embedding = semanticEnabled ? EmbedQueryAsync(ownerId, query, cancellationToken) : null;
            var lexical = await repository.SearchLexicalAsync(ownerId, MemoryQuery.Parse(query), kind,
                CandidateLimit, cancellationToken);
            var semantic = embedding is null
                ? []
                : await SearchSemanticAsync(ownerId, await embedding, kind, cancellationToken);
            activity?.SetTag("jarvis.memory.semantic_hits", semantic.Count);
            activity?.SetTag("jarvis.memory.lexical_hits", lexical.Count);

            hits = MemoryRanking.Rank(lexical, semantic, DateTimeOffset.UtcNow);
            activity?.SetTag("jarvis.memory.hit_count", hits.Count);
            return hits;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception exception)
        {
            outcome = "failed";
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            var tags = new TagList { { "search.outcome", outcome } };
            MemoryDiagnostics.SearchDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
            MemoryDiagnostics.SearchResults.Add(hits.Count, tags);
        }
    }

    public Task RecordRecallAsync(Guid ownerId, IReadOnlyCollection<Guid> memoryIds,
        CancellationToken cancellationToken) =>
        memoryIds.Count == 0 ? Task.CompletedTask : repository.RecordAccessAsync(ownerId, memoryIds, cancellationToken);

    private async Task<MemoryEmbedding?> EmbedQueryAsync(Guid ownerId, string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var embeddings = await embedder!.EmbedAsync(ownerId, [query], cancellationToken);
            return embeddings is [var embedding] ? embedding : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            // Keyword search still answers when the embedding provider is unavailable.
            Activity.Current?.AddEvent(new ActivityEvent("jarvis.memory.semantic_unavailable"));
            return null;
        }
    }

    private async Task<IReadOnlyList<MemorySearchHit>> SearchSemanticAsync(Guid ownerId, MemoryEmbedding? embedding,
        string? kind, CancellationToken cancellationToken)
    {
        if (embedding is null) return [];
        try
        {
            return await index!.SearchSemanticAsync(ownerId, embedding, kind, MinimumSemanticSimilarity,
                CandidateLimit, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            Activity.Current?.AddEvent(new ActivityEvent("jarvis.memory.semantic_unavailable"));
            return [];
        }
    }

    private static void Validate(string kind, string content, float importance, float confidence)
    {
        if (!MemoryKinds.IsValid(kind)) throw new ArgumentException("Unknown memory kind.", nameof(kind));
        if (string.IsNullOrWhiteSpace(content) || content.Length > 8_000) throw new ArgumentException("Memory content must contain 1 to 8,000 characters.", nameof(content));
        if (importance is < 0 or > 1 || confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(importance), "Importance and confidence must be between zero and one.");
    }
}
