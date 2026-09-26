using Jarvis.Domain.Memory;

namespace Jarvis.Application.Memory;

public interface IMemoryRepository
{
    Task<bool> HasActiveMemoriesAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
        string? sourceType, Guid? sourceId, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken);
    Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content, float importance,
        float confidence, string? sourceType, Guid? sourceId, CancellationToken cancellationToken);
    Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryRecord> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> SearchTextAsync(Guid ownerId, string query, string? kind, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> SearchTrigramAsync(Guid ownerId, string query, string? kind, CancellationToken cancellationToken);
}

public interface IMemoryService
{
    Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken,
        string sourceType = "user", Guid? sourceId = null);
    Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
        float importance, float confidence, CancellationToken cancellationToken,
        string sourceType = "conversation", Guid? sourceId = null);
    Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryRecord> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query, CancellationToken cancellationToken, string? kind = null);
}

public interface IConversationMemoryExtractor
{
    Task ExtractAndStoreAsync(Guid ownerId, Guid sourceMessageId, string userMessage, CancellationToken cancellationToken);
}
