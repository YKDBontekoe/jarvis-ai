using Jarvis.Application.Files;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Files;
using Microsoft.EntityFrameworkCore;
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
}

public sealed class FileContentRepository(JarvisDbContext db) : IFileContentRepository
{
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
            Embedding = null
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileSearchHit>> SearchTextAsync(Guid ownerId, string query, CancellationToken cancellationToken)
    {
        var chunks = await db.FileContentChunks.FromSqlInterpolated($"""
            SELECT c.* FROM file_content_chunks c
            JOIN files f ON f."Id" = c.file_id AND f.owner_id = c.owner_id
            WHERE c.owner_id = {ownerId}
              AND f.processing_status = 'ready'
              AND c.search_text @@ websearch_to_tsquery('simple', {query})
            ORDER BY ts_rank(c.search_text, websearch_to_tsquery('simple', {query})) DESC
            LIMIT 20
            """).AsNoTracking().ToListAsync(cancellationToken);
        return await AddFileNamesAsync(ownerId, chunks, cancellationToken);
    }

    public Task DeleteChunksAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken) =>
        db.FileContentChunks.Where(x => x.FileId == fileId && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);

    private async Task<IReadOnlyList<FileSearchHit>> AddFileNamesAsync(Guid ownerId,
        IReadOnlyList<FileContentChunkEntity> chunks, CancellationToken cancellationToken)
    {
        var ids = chunks.Select(x => x.FileId).Distinct().ToArray();
        var names = await db.Files.AsNoTracking().Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FileName, cancellationToken);
        return chunks.Where(x => names.ContainsKey(x.FileId))
            .Select(x => new FileSearchHit(x.FileId, names[x.FileId], x.ChunkIndex, x.Content, 0)).ToArray();
    }
}
