using Jarvis.Application.Memory;
using Jarvis.Domain.Files;

namespace Jarvis.Application.Files;

public interface IFileRepository
{
    Task<StoredFile> CreateAsync(StoredFile file, CancellationToken cancellationToken);
    Task<StoredFile?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> ListQueuedForProcessingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> ListDeletingAsync(CancellationToken cancellationToken);
    Task MarkProcessingScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> RequeueForProcessingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<int> RequeueStaleQueuedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
    Task<int> RequeueStaleProcessingAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
    Task<bool> MarkDeletingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> SetProcessingStatusAsync(Guid id, Guid ownerId, string status, CancellationToken cancellationToken);
    /// <summary>Records whether the file's text was cut off by the extraction or chunk limits.</summary>
    Task SetPartiallyIndexedAsync(Guid id, Guid ownerId, bool partiallyIndexed, CancellationToken cancellationToken);
}

public sealed record FileContentChunk(
    Guid FileId,
    Guid OwnerId,
    int Index,
    string Content,
    int StartOffset = 0,
    int EndOffset = 0,
    int? PageNumber = null,
    string? Heading = null);

public sealed record FileSearchHit(
    Guid FileId,
    string FileName,
    Guid ChunkId,
    int ChunkIndex,
    string Content,
    double Score,
    int ExcerptStart,
    int ExcerptEnd,
    int? PageNumber,
    string SanitizedExcerpt);

/// <summary>A chunk waiting for its embedding, with the context that goes into the embedded text.</summary>
public sealed record FileChunkForIndex(Guid ChunkId, Guid FileId, string FileName, string? Heading, int? PageNumber,
    string Content);

public static class FileChunkText
{
    /// <summary>
    /// What gets embedded: the file name, page and nearest heading in front of the passage, so a chunk that never
    /// repeats its document's subject ("the tenant may end it with one month's notice") still matches a question about
    /// that document ("lease notice period").
    /// </summary>
    public static string ForIndex(FileChunkForIndex chunk)
    {
        var context = "Document: " + chunk.FileName;
        if (chunk.PageNumber is { } page) context += ", page " + page;
        if (!string.IsNullOrWhiteSpace(chunk.Heading)) context += "\nSection: " + chunk.Heading;
        return context + "\n\n" + chunk.Content;
    }
}

public interface IFileContentRepository
{
    Task ReplaceChunksAsync(Guid fileId, Guid ownerId, IReadOnlyList<FileContentChunk> chunks, CancellationToken cancellationToken);

    /// <summary>
    /// Keyword search over ready files. The query is analysed like memory search (<see cref="MemoryQuery"/>): terms
    /// are OR-ed and weighted BM25-style, so a full question still matches; when few chunks match, pg_trgm catches
    /// typos. Each hit's score is normalised so 1 means every original term matched.
    /// </summary>
    Task<IReadOnlyList<FileSearchHit>> SearchTextAsync(Guid ownerId, string query, CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? fileIds = null, int limit = 20);

    /// <summary>Nearest chunks by cosine similarity among chunks embedded with the query's model.</summary>
    Task<IReadOnlyList<FileSearchHit>> SearchSemanticAsync(Guid ownerId, MemoryEmbedding query,
        IReadOnlyCollection<Guid>? fileIds, double minimumSimilarity, int limit, CancellationToken cancellationToken);

    /// <summary>Whether any ready chunk in scope is embedded with this model; guards the paid query embedding.</summary>
    Task<bool> HasEmbeddedChunksAsync(Guid ownerId, string model, IReadOnlyCollection<Guid>? fileIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListOwnersNeedingEmbeddingAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileChunkForIndex>> ListChunksNeedingEmbeddingAsync(Guid ownerId, string model, int limit,
        CancellationToken cancellationToken);
    Task SetChunkEmbeddingAsync(Guid chunkId, Guid ownerId, MemoryEmbedding embedding, CancellationToken cancellationToken);
    Task DeleteChunksAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken);
}

public interface IFileSearchService
{
    Task<IReadOnlyList<FileSearchHit>> SearchAsync(Guid ownerId, string query, CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? fileIds = null, int maxHits = FileRanking.MaxHits);
}

public interface IConversationFileScopeService
{
    Task<IReadOnlyCollection<Guid>?> ResolveSearchFileIdsAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken);
}

public interface IFileProcessingScheduler
{
    Task ScheduleAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken);
    Task CancelAsync(Guid fileId, CancellationToken cancellationToken);
}

public interface IFileMalwareScanner
{
    Task ScanAsync(Stream content, CancellationToken cancellationToken);
}

public sealed class MalwareDetectedException() : Exception("The uploaded file was rejected by malware scanning.");

public sealed record FileProcessingInput(Guid FileId, Guid OwnerId);

public interface IObjectStorage
{
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream?> GetAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public interface IFileService
{
    Task<StoredFile?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<StoredFile> UploadAsync(Guid ownerId, string fileName, string contentType, long length,
        Stream content, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<(StoredFile File, Stream Content)?> OpenReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> RetryIndexingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}
