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
        CancellationToken cancellationToken, Guid? profileId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var memory = new MemoryEntity
        {
            Id = Guid.CreateVersion7(), OwnerId = ownerId, Kind = kind, Content = content,
            Importance = importance, Confidence = confidence, Embedding = null, EmbeddingModel = null, GraphIndexedAt = null,
            SourceType = sourceType, SourceId = sourceId, CreatedAt = now, UpdatedAt = now,
            ValidUntil = validUntil, IsPinned = isPinned, ProfileId = profileId
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
            ValidUntil = null, IsPinned = false, ProfileId = existing.ProfileId,
            AccessCount = existing.AccessCount, LastAccessedAt = existing.LastAccessedAt
        };
        db.Memories.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await db.GraphRelations
            .Where(x => x.OwnerId == ownerId && x.SourceMemoryId == existing.Id && x.ValidTo == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ValidTo, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement.ToRecord();
    }

    public async Task<MemoryRecord?> ExpireAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var memory = await db.Memories.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId &&
            (x.ValidUntil == null || x.ValidUntil > DateTimeOffset.UtcNow), cancellationToken);
        if (memory is null || memory.IsPinned) return null;

        var now = DateTimeOffset.UtcNow;
        memory.ValidUntil = now;
        memory.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await db.GraphRelations
            .Where(x => x.OwnerId == ownerId && x.SourceMemoryId == memory.Id && x.ValidTo == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ValidTo, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return memory.ToRecord();
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
        memory.SearchHints = null;
        // Indexing writes these columns with ExecuteUpdate, which bypasses change tracking, so mark them explicitly.
        foreach (var property in new[] { nameof(MemoryEntity.Embedding), nameof(MemoryEntity.EmbeddingModel), nameof(MemoryEntity.SearchHints) })
            db.Entry(memory).Property(property).IsModified = true;
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

    public async Task RecordAccessAsync(Guid ownerId, IReadOnlyCollection<Guid> memoryIds,
        CancellationToken cancellationToken)
    {
        if (memoryIds.Count == 0) return;
        var ids = memoryIds.Distinct().ToArray();
        var now = DateTimeOffset.UtcNow;
        await db.Memories.Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.AccessCount, x => x.AccessCount + 1)
                .SetProperty(x => x.LastAccessedAt, now), cancellationToken);
        // The bulk update bypasses the change tracker; refresh tracked copies so a later replace carries the counts.
        foreach (var entry in db.ChangeTracker.Entries<MemoryEntity>().Where(entry => ids.Contains(entry.Entity.Id)).ToArray())
            await entry.ReloadAsync(cancellationToken);
    }

    /// <summary>
    /// One round trip for keyword and fuzzy matching. Every query term is OR-ed and weighted by its inverse document
    /// frequency within the owner's active memories (BM25-style), so a rare word such as a name counts for more than
    /// a common one. Scores are normalised by the weight of the original terms, which keeps them comparable across
    /// queries: 1 means every original term matched. Fuzzy matching (pg_trgm word similarity) only scores original
    /// terms the keyword match missed, which catches typos and Dutch compounds (gesprek in salarisgesprek). Each term
    /// is looked up separately so PostgreSQL can use the tsvector and trigram GIN indexes instead of scanning every
    /// memory the owner has.
    /// </summary>
    public async Task<IReadOnlyList<MemoryLexicalMatch>> SearchLexicalAsync(Guid ownerId, MemoryQuery query,
        string? kind, int limit, CancellationToken cancellationToken)
    {
        if (query.IsEmpty) return [];
        var terms = query.Terms.Select(term => term.Term).ToArray();
        var tsQueries = query.Terms.Select(term => term.TsQuery).ToArray();
        var weights = query.Terms.Select(term => term.Weight).ToArray();
        // Per-term lookups take their term from a lateral join, which PostgreSQL costs poorly; without this it scans
        // every memory for the trigram step (about 7x slower at 2,000 memories). SET LOCAL ends with the transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", cancellationToken);
        var rows = await db.Database.SqlQuery<LexicalRow>($"""
            WITH terms AS (
                SELECT t.term, to_tsquery('simple', t.query) AS query, t.weight
                FROM unnest({terms}::text[], {tsQueries}::text[], {weights}::float8[]) AS t(term, query, weight)
            ),
            population AS (
                SELECT count(*)::float8 AS n, greatest(avg(length(content)), 1)::float8 AS average_length FROM memories
                WHERE owner_id = {ownerId}
                  AND (CAST({kind} AS text) IS NULL OR kind = CAST({kind} AS text))
                  AND (valid_until IS NULL OR valid_until > now())
            ),
            text_hits AS (
                SELECT hit.id, terms.term FROM terms CROSS JOIN LATERAL (
                    SELECT "Id" AS id FROM memories
                    WHERE owner_id = {ownerId}
                      AND (CAST({kind} AS text) IS NULL OR kind = CAST({kind} AS text))
                      AND (valid_until IS NULL OR valid_until > now())
                      AND search_vector @@ terms.query) AS hit
            ),
            fuzzy_hits AS (
                -- OFFSET 0 keeps the owner filters out of the lookup so the trigram index drives it.
                SELECT hit.id, terms.term, word_similarity(terms.term, hit.content) AS similarity
                FROM terms CROSS JOIN LATERAL (
                    SELECT "Id" AS id, owner_id, kind, valid_until, content FROM memories
                    WHERE content %> terms.term OFFSET 0) AS hit
                WHERE terms.weight >= 1 AND length(terms.term) >= 5
                  AND hit.owner_id = {ownerId}
                  AND (CAST({kind} AS text) IS NULL OR hit.kind = CAST({kind} AS text))
                  AND (hit.valid_until IS NULL OR hit.valid_until > now())
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
                       text_scores.score / (0.75 + 0.25 * length(memories.content) / population.average_length) AS score
                FROM text_scores JOIN memories ON memories."Id" = text_scores.id CROSS JOIN population
            ),
            fuzzy_scores AS (
                SELECT fuzzy_hits.id, sum((fuzzy_hits.similarity - 0.5) / 0.5 * stats.idf) AS score
                FROM fuzzy_hits JOIN stats ON stats.term = fuzzy_hits.term
                WHERE NOT EXISTS (SELECT 1 FROM text_hits
                                  WHERE text_hits.id = fuzzy_hits.id AND text_hits.term = fuzzy_hits.term)
                GROUP BY fuzzy_hits.id
            ),
            scored AS (
                SELECT coalesce(text_scores_normalised.id, fuzzy_scores.id) AS id,
                       coalesce(text_scores_normalised.score, 0) AS text_score, coalesce(fuzzy_scores.score, 0) AS fuzzy_score
                FROM text_scores_normalised FULL JOIN fuzzy_scores ON fuzzy_scores.id = text_scores_normalised.id
            )
            SELECT m."Id" AS "Id", m.owner_id AS "OwnerId", m.kind AS "Kind", m.content AS "Content",
                   m.importance AS "Importance", m.confidence AS "Confidence", m.source_type AS "SourceType",
                   m.source_id AS "SourceId", m.created_at AS "CreatedAt", m.updated_at AS "UpdatedAt",
                   m.valid_until AS "ValidUntil", m.is_pinned AS "IsPinned", m.profile_id AS "ProfileId",
                   m.access_count AS "AccessCount", m.last_accessed_at AS "LastAccessedAt",
                   scored.text_score / total.value AS "TextScore", scored.fuzzy_score / total.value AS "FuzzyScore"
            FROM scored JOIN memories m ON m."Id" = scored.id CROSS JOIN total
            ORDER BY scored.text_score + 0.5 * scored.fuzzy_score DESC, m.importance DESC
            LIMIT {limit}
            """).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows.Select(row => new MemoryLexicalMatch(new MemoryRecord(row.Id, row.OwnerId, row.Kind, row.Content,
                row.Importance, row.Confidence, row.SourceType, row.SourceId, row.CreatedAt, row.UpdatedAt,
                row.ValidUntil, row.IsPinned, row.ProfileId, row.AccessCount, row.LastAccessedAt),
            row.TextScore, row.FuzzyScore)).ToArray();
    }

    private sealed class LexicalRow
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public float Importance { get; init; }
        public float Confidence { get; init; }
        public string? SourceType { get; init; }
        public Guid? SourceId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? ValidUntil { get; init; }
        public bool IsPinned { get; init; }
        public Guid? ProfileId { get; init; }
        public int AccessCount { get; init; }
        public DateTimeOffset? LastAccessedAt { get; init; }
        public double TextScore { get; init; }
        public double FuzzyScore { get; init; }
    }
}
