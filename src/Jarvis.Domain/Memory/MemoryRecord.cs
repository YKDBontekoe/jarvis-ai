namespace Jarvis.Domain.Memory;

public sealed record MemoryRecord(
    Guid Id,
    Guid OwnerId,
    string Kind,
    string Content,
    float Importance,
    float Confidence,
    string? SourceType,
    Guid? SourceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ValidUntil,
    bool IsPinned);

public sealed record MemorySearchHit(MemoryRecord Memory, double Score);
