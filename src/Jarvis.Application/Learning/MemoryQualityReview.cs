namespace Jarvis.Application.Learning;

/// <summary>A read-only critique. Findings are suggestions, never executable instructions or evidence.</summary>
public sealed record MemoryQualityReview(DateTimeOffset ReviewedAt, string Status, string Model,
    int MemoryCount, int EntityCount, bool Partial, IReadOnlyList<MemoryQualityFinding> Findings);

public sealed record MemoryQualityFinding(string Kind, string Severity, IReadOnlyList<Guid> MemoryIds,
    IReadOnlyList<Guid> EntityIds, string Reason, string Recommendation);
