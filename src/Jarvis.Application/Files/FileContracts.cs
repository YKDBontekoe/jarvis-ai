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
}

public sealed record FileContentChunk(
    Guid FileId,
    Guid OwnerId,
    int Index,
    string Content,
    int StartOffset = 0,
    int EndOffset = 0,
    int? PageNumber = null);

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

public interface IFileContentRepository
{
    Task ReplaceChunksAsync(Guid fileId, Guid ownerId, IReadOnlyList<FileContentChunk> chunks, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileSearchHit>> SearchTextAsync(Guid ownerId, string query, FileSearchScope scope,
        CancellationToken cancellationToken);
    Task DeleteChunksAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken);
}

public interface IFileSearchService
{
    Task<IReadOnlyList<FileSearchHit>> SearchAsync(Guid ownerId, string query, FileSearchScope scope,
        CancellationToken cancellationToken);
}

public interface IConversationFileScopeService
{
    Task<FileSearchScope> GetSearchScopeAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
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
    Task<StoredFile> UploadAsync(Guid ownerId, string fileName, string contentType, long length,
        Stream content, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<(StoredFile File, Stream Content)?> OpenReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> RetryIndexingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}
