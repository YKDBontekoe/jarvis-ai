using Jarvis.Domain.Memory;

namespace Jarvis.Application.Memory;

public interface IMemoryRepository
{
    Task<bool> HasActiveMemoriesAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
        string? sourceType, Guid? sourceId, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken,
        Guid? profileId = null);
    Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content, float importance,
        float confidence, string? sourceType, Guid? sourceId, CancellationToken cancellationToken);
    Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryLexicalMatch>> SearchLexicalAsync(Guid ownerId, MemoryQuery query, string? kind, int limit,
        CancellationToken cancellationToken);
    /// <summary>Counts a recall (the memory reached the model) so ranking and dreaming can favour useful memories.</summary>
    Task RecordAccessAsync(Guid ownerId, IReadOnlyCollection<Guid> memoryIds, CancellationToken cancellationToken);
}

/// <summary>A keyword match with normalised keyword (0-1+) and fuzzy scores.</summary>
public sealed record MemoryLexicalMatch(MemoryRecord Memory, double TextScore, double FuzzyScore);

public interface IMemoryService
{
    Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken,
        string sourceType = "user", Guid? sourceId = null, Guid? profileId = null);
    Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
        float importance, float confidence, CancellationToken cancellationToken,
        string sourceType = "conversation", Guid? sourceId = null);
    Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query, CancellationToken cancellationToken, string? kind = null,
        int maxHits = MemoryRanking.MaxHits);
    Task RecordRecallAsync(Guid ownerId, IReadOnlyCollection<Guid> memoryIds, CancellationToken cancellationToken);
}

public interface IConversationMemoryExtractor
{
    Task ExtractAndStoreAsync(Guid ownerId, Guid sourceMessageId, string userMessage, CancellationToken cancellationToken,
        Guid? profileId = null);
}

public static class MemoryKinds
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { "preference", "fact", "decision", "project", "event", "relationship", "technical", "routine", "journal", "other" };

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? kind) =>
        kind is not null && All.Contains(kind);

    public static bool IsValidFilter(string? kind) => kind is null || All.Contains(kind);
}

public static class MemoryValidity
{
    public static DateTimeOffset? ForUpdate(DateTimeOffset? validUntil) =>
        validUntil is { } until && until <= DateTimeOffset.UtcNow ? null : validUntil;
}
