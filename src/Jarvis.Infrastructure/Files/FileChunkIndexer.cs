using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Files;

/// <summary>
/// Embeds file chunks with the owner's embedding model so file search can use semantic retrieval. It backfills files
/// indexed before embeddings existed and re-embeds chunks after the model changes. Without an embedding model it does
/// nothing and file search stays keyword-only.
/// </summary>
public sealed class FileChunkIndexer(IFileContentRepository chunks, IMemoryEmbedder embedder,
    ILogger<FileChunkIndexer> logger)
{
    internal const int BatchSize = 32;
    /// <summary>Batches per pass: a 300,000-character file (about 215 chunks) is embedded within one or two passes.</summary>
    internal const int MaxBatchesPerPass = 8;

    public async Task<int> IndexOwnerAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var model = await embedder.ResolveAsync(ownerId, cancellationToken);
        if (model is null) return 0;
        var embedded = 0;
        for (var batch = 0; batch < MaxBatchesPerPass; batch++)
        {
            var pending = await chunks.ListChunksNeedingEmbeddingAsync(ownerId, model.Model, BatchSize, cancellationToken);
            if (pending.Count == 0) break;
            var results = await EmbedBatchAsync(model, pending, cancellationToken);
            foreach (var (chunkId, embedding) in results)
                await chunks.SetChunkEmbeddingAsync(chunkId, ownerId, embedding, cancellationToken);
            embedded += results.Count;
            // A batch where nothing could be embedded would come back unchanged; try again on the next pass.
            if (results.Count == 0 || pending.Count < BatchSize) break;
        }
        return embedded;
    }

    /// <summary>
    /// Embeds the batch in one call. When the provider rejects it, chunks are embedded one by one so a single bad
    /// chunk cannot keep the queue stuck.
    /// </summary>
    private async Task<IReadOnlyList<(Guid ChunkId, MemoryEmbedding Embedding)>> EmbedBatchAsync(IResolvedEmbedder model,
        IReadOnlyList<FileChunkForIndex> pending, CancellationToken cancellationToken)
    {
        try
        {
            var vectors = await model.EmbedAsync(pending.Select(FileChunkText.ForIndex).ToArray(), cancellationToken);
            if (vectors.Count == pending.Count)
                return pending.Zip(vectors, (chunk, vector) => (chunk.ChunkId, vector)).ToArray();
            logger.LogWarning("Embedding provider returned {Returned} vectors for {Count} file chunks.", vectors.Count,
                pending.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && pending.Count > 1)
        {
            logger.LogWarning(exception, "Embedding a batch of {Count} file chunks failed; retrying one by one.",
                pending.Count);
        }
        var results = new List<(Guid, MemoryEmbedding)>();
        foreach (var chunk in pending)
        {
            try
            {
                var single = await model.EmbedAsync([FileChunkText.ForIndex(chunk)], cancellationToken);
                if (single is [var vector]) results.Add((chunk.ChunkId, vector));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not embed file chunk {ChunkId}; it stays queued.", chunk.ChunkId);
            }
        }
        return results;
    }
}
