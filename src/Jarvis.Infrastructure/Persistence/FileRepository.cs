using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Text.Json;

namespace Jarvis.Infrastructure.Persistence;

public sealed class FileRepository(JarvisDbContext db) : IFileRepository
{
    public async Task<StoredFile> CreateAsync(StoredFile file, CancellationToken cancellationToken)
    {
        var entity = new StoredFileEntity
        {
            Id = file.Id,
            OwnerId = file.OwnerId,
            ObjectKey = file.ObjectKey,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            Sha256 = file.Sha256,
            CreatedAt = file.CreatedAt,
            ProcessingStatus = file.ProcessingStatus
        };
        db.Files.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<StoredFile?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<StoredFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Files.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<StoredFile>> ListQueuedForProcessingAsync(CancellationToken cancellationToken) =>
        (await db.Files.AsNoTracking().Where(x => x.ProcessingStatus == "queued" && x.ScheduleDispatchedAt == null)
            .OrderBy(x => x.CreatedAt).Take(500).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<StoredFile>> ListDeletingAsync(CancellationToken cancellationToken) =>
        (await db.Files.AsNoTracking().Where(x => x.ProcessingStatus == "deleting")
            .OrderBy(x => x.CreatedAt).Take(500).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task MarkProcessingScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await db.Files.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (file is null) return;
        file.ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RequeueForProcessingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Files.Where(x => x.Id == id && x.OwnerId == ownerId && x.ProcessingStatus != "deleting")
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ProcessingStatus, "queued")
                .SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null), cancellationToken) != 0;

    public async Task<int> RequeueStaleQueuedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        await db.Files.Where(x => x.ProcessingStatus == "queued" &&
                x.ScheduleDispatchedAt != null && x.ScheduleDispatchedAt < olderThan)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null), cancellationToken);

    public async Task<int> RequeueStaleProcessingAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        await db.Files.Where(x => x.ProcessingStatus == "processing" &&
                x.ScheduleDispatchedAt != null && x.ScheduleDispatchedAt < olderThan)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ProcessingStatus, "queued")
                .SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null), cancellationToken);

    public async Task<bool> MarkDeletingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Files.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ProcessingStatus, "deleting"), cancellationToken) != 0;

    public async Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.Files.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0) return;
        db.AuditEvents.Add(new AuditEvent(ownerId, "files", "file.deleted", "high", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = id })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> SetProcessingStatusAsync(Guid id, Guid ownerId, string status, CancellationToken cancellationToken)
    {
        var query = db.Files.Where(x => x.Id == id && x.OwnerId == ownerId);
        if (status == "processing")
        {
            query = query.Where(x => x.ProcessingStatus == "queued" || x.ProcessingStatus == "processing"
                || x.ProcessingStatus == "failed");
            var startedAt = DateTimeOffset.UtcNow;
            return await query.ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.ProcessingStatus, status)
                    .SetProperty(x => x.ScheduleDispatchedAt, startedAt),
                cancellationToken) != 0;
        }

        if (status is "ready" or "failed")
            query = query.Where(x => x.ProcessingStatus == "processing");
        else
            query = query.Where(x => x.ProcessingStatus != "deleting" || status == "deleting");
        return await query.ExecuteUpdateAsync(update => update.SetProperty(x => x.ProcessingStatus, status),
            cancellationToken) != 0;
    }

    public Task SetPartiallyIndexedAsync(Guid id, Guid ownerId, bool partiallyIndexed,
        CancellationToken cancellationToken) =>
        db.Files.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.PartiallyIndexed, partiallyIndexed), cancellationToken);
}

public sealed class FileContentRepository(JarvisDbContext db) : IFileContentRepository
{
    private const int MaxHeadingLength = 200;
    private const int FuzzyCandidateLimit = 100;
    private const int FuzzyFallbackBelow = 3;

    public async Task ReplaceChunksAsync(Guid fileId, Guid ownerId, IReadOnlyList<FileContentChunk> chunks,
        CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == fileId && x.OwnerId == ownerId, cancellationToken);
        if (file is null)
            throw new InvalidOperationException("The file no longer exists for this owner.");
        if (file.ProcessingStatus == "deleting") return;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.FileContentChunks.Where(x => x.FileId == fileId && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        db.FileContentChunks.AddRange(chunks.Select(chunk => new FileContentChunkEntity
        {
            Id = Guid.CreateVersion7(),
            FileId = fileId,
            OwnerId = ownerId,
            ChunkIndex = chunk.Index,
            Content = chunk.Content,
            StartOffset = chunk.StartOffset,
            EndOffset = chunk.EndOffset,
            PageNumber = chunk.PageNumber,
            Heading = string.IsNullOrWhiteSpace(chunk.Heading) ? null
                : chunk.Heading.Length <= MaxHeadingLength ? chunk.Heading : chunk.Heading[..MaxHeadingLength],
            Embedding = null,
            EmbeddingModel = null
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The memory keyword search (<see cref="MemoryRepository.SearchLexicalAsync"/>) applied to file chunks: OR-ed
    /// terms weighted by their inverse document frequency among the owner's ready chunks in scope, BM25-style length
    /// normalisation, and, when the keywords find fewer than three chunks, pg_trgm word similarity for original terms
    /// that no chunk contains. Each term is looked up
    /// separately so the tsvector and trigram GIN indexes drive the query.
    /// </summary>
    public async Task<IReadOnlyList<FileSearchHit>> SearchTextAsync(Guid ownerId, string query,
        CancellationToken cancellationToken, IReadOnlyCollection<Guid>? fileIds = null, int limit = 20)
    {
        if (fileIds is { Count: 0 }) return [];
        var parsed = MemoryQuery.Parse(query);
        if (parsed.IsEmpty) return [];
        var terms = parsed.Terms.Select(term => term.Term).ToArray();
        var tsQueries = parsed.Terms.Select(term => term.TsQuery).ToArray();
        var weights = parsed.Terms.Select(term => term.Weight).ToArray();
        var scoped = fileIds is not null;
        var ids = fileIds?.Distinct().ToArray() ?? [];
        // See MemoryRepository: lateral per-term lookups are costed poorly, so sequential scans are turned off for
        // this transaction only.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", cancellationToken);
        var rows = await db.Database.SqlQuery<LexicalRow>($"""
            WITH terms AS (
                SELECT t.term, to_tsquery('simple', t.query) AS query, t.weight
                FROM unnest({terms}::text[], {tsQueries}::text[], {weights}::float8[]) AS t(term, query, weight)
            ),
            ready AS (
                SELECT f."Id" AS id FROM files f
                WHERE f.owner_id = {ownerId} AND f.processing_status = 'ready'
                  AND (NOT {scoped} OR f."Id" = ANY({ids}::uuid[]))
            ),
            population AS (
                SELECT count(*)::float8 AS n, greatest(avg(length(c.content)), 1)::float8 AS average_length
                FROM file_content_chunks c JOIN ready ON ready.id = c.file_id
                WHERE c.owner_id = {ownerId}
            ),
            text_hits AS (
                SELECT hit.id, terms.term FROM terms CROSS JOIN LATERAL (
                    SELECT c."Id" AS id FROM file_content_chunks c JOIN ready ON ready.id = c.file_id
                    WHERE c.owner_id = {ownerId} AND c.search_text @@ terms.query) AS hit
            ),
            fuzzy_terms AS (
                -- Chunks are long, so checking word similarity costs far more than for memories. It is a fallback:
                -- it runs only when the keyword match found few chunks, for original terms no chunk contains (typos,
                -- parts of compounds), on a bounded set of candidates.
                SELECT terms.term FROM terms
                WHERE terms.weight >= 1 AND length(terms.term) >= 5
                  AND NOT EXISTS (SELECT 1 FROM text_hits WHERE text_hits.term = terms.term)
                  AND (SELECT count(DISTINCT id) FROM text_hits) < {FuzzyFallbackBelow}
            ),
            fuzzy_hits AS (
                SELECT hit.id, fuzzy_terms.term, word_similarity(fuzzy_terms.term, hit.content) AS similarity
                FROM fuzzy_terms CROSS JOIN LATERAL (
                    SELECT c."Id" AS id, c.file_id, c.content FROM file_content_chunks c
                    WHERE c.owner_id = {ownerId} AND c.content %> fuzzy_terms.term
                    LIMIT {FuzzyCandidateLimit}) AS hit
                WHERE hit.file_id IN (SELECT id FROM ready)
            ),
            stats AS (
                SELECT terms.term, terms.weight,
                       ln(1 + (population.n - coalesce(df.count, 0) + 0.5) / (coalesce(df.count, 0) + 0.5)) AS idf
                FROM terms CROSS JOIN population
                LEFT JOIN (SELECT term, count(*) AS count FROM text_hits GROUP BY term) AS df ON df.term = terms.term
            ),
            total AS (SELECT greatest(coalesce(sum(weight * idf) FILTER (WHERE weight >= 1), 0), 1e-6) AS value FROM stats),
            text_scores AS (
                SELECT text_hits.id, sum(stats.weight * stats.idf) AS score
                FROM text_hits JOIN stats ON stats.term = text_hits.term GROUP BY text_hits.id
            ),
            text_scores_normalised AS (
                SELECT text_scores.id,
                       text_scores.score / (0.75 + 0.25 * length(c.content) / population.average_length) AS score
                FROM text_scores JOIN file_content_chunks c ON c."Id" = text_scores.id CROSS JOIN population
            ),
            fuzzy_scores AS (
                SELECT fuzzy_hits.id, sum((fuzzy_hits.similarity - 0.5) / 0.5 * stats.idf) AS score
                FROM fuzzy_hits JOIN stats ON stats.term = fuzzy_hits.term
                GROUP BY fuzzy_hits.id
            ),
            scored AS (
                SELECT coalesce(text_scores_normalised.id, fuzzy_scores.id) AS id,
                       coalesce(text_scores_normalised.score, 0) AS text_score, coalesce(fuzzy_scores.score, 0) AS fuzzy_score
                FROM text_scores_normalised FULL JOIN fuzzy_scores ON fuzzy_scores.id = text_scores_normalised.id
            )
            SELECT c."Id" AS "Id", c.file_id AS "FileId", c.chunk_index AS "ChunkIndex", c.content AS "Content",
                   c.page_number AS "PageNumber",
                   (scored.text_score + 0.5 * scored.fuzzy_score) / total.value AS "Score"
            FROM scored JOIN file_content_chunks c ON c."Id" = scored.id CROSS JOIN total
            ORDER BY scored.text_score + 0.5 * scored.fuzzy_score DESC, c.chunk_index
            LIMIT {limit}
            """).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToHitsAsync(ownerId, rows.Select(row => (row.Id, row.FileId, row.ChunkIndex, row.Content,
            row.PageNumber, row.Score)).ToArray(), cancellationToken);
    }

    public async Task<IReadOnlyList<FileSearchHit>> SearchSemanticAsync(Guid ownerId, MemoryEmbedding query,
        IReadOnlyCollection<Guid>? fileIds, double minimumSimilarity, int limit, CancellationToken cancellationToken)
    {
        if (fileIds is { Count: 0 }) return [];
        var vector = new Vector(query.Vector);
        var chunks = Ready(ownerId, fileIds).Where(x => x.Embedding != null && x.EmbeddingModel == query.Model);
        // The owner, scope and model filters are applied after the HNSW scan; iterative scans keep reading the
        // index until enough rows pass them instead of returning a short list (pgvector 0.8+, ignored before).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = relaxed_order", cancellationToken);
        var hits = await chunks
            .Select(x => new { x.Id, x.FileId, x.ChunkIndex, x.Content, x.PageNumber, Distance = x.Embedding!.CosineDistance(vector) })
            .OrderBy(x => x.Distance)
            .Take(limit)
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToHitsAsync(ownerId, hits.Where(hit => 1 - hit.Distance >= minimumSimilarity)
            .OrderBy(hit => hit.Distance)
            .Select(hit => (hit.Id, hit.FileId, hit.ChunkIndex, hit.Content, hit.PageNumber, 1 - hit.Distance))
            .ToArray(), cancellationToken);
    }

    public Task<bool> HasEmbeddedChunksAsync(Guid ownerId, string model, IReadOnlyCollection<Guid>? fileIds,
        CancellationToken cancellationToken) =>
        fileIds is { Count: 0 }
            ? Task.FromResult(false)
            : Ready(ownerId, fileIds).AnyAsync(x => x.Embedding != null && x.EmbeddingModel == model, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListOwnersNeedingEmbeddingAsync(int limit, CancellationToken cancellationToken) =>
        await db.FileContentChunks.AsNoTracking().Where(x => x.Embedding == null)
            .Select(x => x.OwnerId).Distinct().Take(limit).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FileChunkForIndex>> ListChunksNeedingEmbeddingAsync(Guid ownerId, string model,
        int limit, CancellationToken cancellationToken) =>
        await (from chunk in Ready(ownerId, null)
               join file in db.Files on chunk.FileId equals file.Id
               where chunk.Embedding == null || chunk.EmbeddingModel != model
               orderby file.CreatedAt descending, chunk.ChunkIndex
               select new FileChunkForIndex(chunk.Id, chunk.FileId, file.FileName, chunk.Heading, chunk.PageNumber,
                   chunk.Content))
            .Take(limit).ToListAsync(cancellationToken);

    public Task SetChunkEmbeddingAsync(Guid chunkId, Guid ownerId, MemoryEmbedding embedding,
        CancellationToken cancellationToken)
    {
        var vector = new Vector(embedding.Vector);
        return db.FileContentChunks.Where(x => x.Id == chunkId && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Embedding, vector)
                .SetProperty(x => x.EmbeddingModel, embedding.Model), cancellationToken);
    }

    public Task DeleteChunksAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken) =>
        db.FileContentChunks.Where(x => x.FileId == fileId && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>The owner's chunks of ready files, limited to <paramref name="fileIds"/> when given.</summary>
    private IQueryable<FileContentChunkEntity> Ready(Guid ownerId, IReadOnlyCollection<Guid>? fileIds)
    {
        var ready = db.FileContentChunks.AsNoTracking().Where(x => x.OwnerId == ownerId &&
            db.Files.Any(file => file.Id == x.FileId && file.OwnerId == ownerId && file.ProcessingStatus == "ready"));
        if (fileIds is null) return ready;
        var ids = fileIds.Distinct().ToArray();
        return ready.Where(x => ids.Contains(x.FileId));
    }

    private async Task<IReadOnlyList<FileSearchHit>> ToHitsAsync(Guid ownerId,
        IReadOnlyList<(Guid Id, Guid FileId, int ChunkIndex, string Content, int? PageNumber, double Score)> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0) return [];
        var ids = chunks.Select(x => x.FileId).Distinct().ToArray();
        var names = await db.Files.AsNoTracking().Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FileName, cancellationToken);
        return chunks.Where(x => names.ContainsKey(x.FileId))
            .Select(x =>
            {
                var sanitized = FileReferenceSanitizer.SanitizeExcerpt(x.Content);
                var excerptEnd = Math.Min(sanitized.Length, x.Content.Length);
                return new FileSearchHit(x.FileId, names[x.FileId], x.Id, x.ChunkIndex, x.Content, x.Score,
                    0, excerptEnd, x.PageNumber, sanitized);
            }).ToArray();
    }

    private sealed class LexicalRow
    {
        public Guid Id { get; init; }
        public Guid FileId { get; init; }
        public int ChunkIndex { get; init; }
        public string Content { get; init; } = string.Empty;
        public int? PageNumber { get; init; }
        public double Score { get; init; }
    }
}
