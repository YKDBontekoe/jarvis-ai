using System.Diagnostics;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;

namespace Jarvis.Infrastructure.Files;

/// <summary>
/// Hybrid file search: keyword retrieval (OR-ed, BM25-style) and, when the owner has an embedding model and embedded
/// chunks in scope, semantic retrieval, fused by <see cref="FileRanking"/>. The query embedding runs while the keyword
/// query does. If the embedding provider fails, keyword results still answer.
/// </summary>
public sealed class FileSearchService(IFileContentRepository repository, IMemoryEmbedder? embedder = null)
    : IFileSearchService
{
    internal const int CandidateLimit = 20;
    internal const double MinimumSemanticSimilarity = 0.3;
    private static readonly ActivitySource Source = new("Jarvis.Files");

    public async Task<IReadOnlyList<FileSearchHit>> SearchAsync(Guid ownerId, string query, CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? fileIds = null, int maxHits = FileRanking.MaxHits)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 2_000) return [];
        if (fileIds is { Count: 0 }) return [];
        using var activity = Source.StartActivity("jarvis.files.search");

        // Resolving the model and the existence check read the scoped database context, so they run first; the
        // provider call does not, and runs alongside the keyword query.
        var model = await ResolveEmbedderAsync(ownerId, fileIds, cancellationToken);
        var pendingEmbedding = model is null ? null : EmbedQueryAsync(model, query, cancellationToken);
        IReadOnlyList<FileSearchHit> lexical;
        try
        {
            lexical = await repository.SearchTextAsync(ownerId, query, cancellationToken, fileIds, CandidateLimit);
        }
        catch
        {
            if (pendingEmbedding is not null) await Task.WhenAny(pendingEmbedding);
            throw;
        }
        var embedding = pendingEmbedding is null ? null : await pendingEmbedding;
        IReadOnlyList<FileSearchHit> semantic = [];
        if (embedding is not null)
        {
            try
            {
                semantic = await repository.SearchSemanticAsync(ownerId, embedding, fileIds, MinimumSemanticSimilarity,
                    CandidateLimit, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                                              !cancellationToken.IsCancellationRequested)
            {
                activity?.AddEvent(new ActivityEvent("jarvis.files.semantic_unavailable"));
            }
        }
        activity?.SetTag("jarvis.files.lexical_hits", lexical.Count);
        activity?.SetTag("jarvis.files.semantic_hits", semantic.Count);
        var hits = FileRanking.Rank(lexical, semantic, Math.Clamp(maxHits, 1, CandidateLimit));
        activity?.SetTag("jarvis.files.hit_count", hits.Count);
        return hits;
    }

    private async Task<IResolvedEmbedder?> ResolveEmbedderAsync(Guid ownerId, IReadOnlyCollection<Guid>? fileIds,
        CancellationToken cancellationToken)
    {
        if (embedder is null) return null;
        try
        {
            var model = await embedder.ResolveAsync(ownerId, cancellationToken);
            // The existence check only guards the paid embedding call.
            return model is not null &&
                   await repository.HasEmbeddedChunksAsync(ownerId, model.Model, fileIds, cancellationToken)
                ? model
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            Activity.Current?.AddEvent(new ActivityEvent("jarvis.files.semantic_unavailable"));
            return null;
        }
    }

    private static async Task<MemoryEmbedding?> EmbedQueryAsync(IResolvedEmbedder model, string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var embeddings = await model.EmbedAsync([query], cancellationToken);
            return embeddings is [var embedding] ? embedding : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            Activity.Current?.AddEvent(new ActivityEvent("jarvis.files.semantic_unavailable"));
            return null;
        }
    }
}
