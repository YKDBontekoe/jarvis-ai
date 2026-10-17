using Jarvis.Domain.Memory;

namespace Jarvis.Application.Memory;

public static class MemoryReviewReasons
{
    /// <summary>Jarvis learned it from a conversation or journal entry lately; the owner never wrote it.</summary>
    public const string Recent = "recent";

    /// <summary>Jarvis was not sure when it saved this.</summary>
    public const string LowConfidence = "low_confidence";

    /// <summary>It stops being current soon.</summary>
    public const string Expiring = "expiring";
}

/// <summary>A memory worth a second look, and why.</summary>
public sealed record MemoryReviewItem(MemoryRecord Memory, IReadOnlyList<string> Reasons);

/// <summary>
/// Picks the memories the owner should look at: recently learned, low confidence, or expiring. Pure, so each rule is
/// tested with a fixed clock. Only current memories are listed; what was replaced is history, reached from the memory
/// that replaced it.
/// </summary>
public static class MemoryReviewRules
{
    public const int MaxItems = 40;
    public const float LowConfidenceBelow = 0.6f;
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);
    public static readonly TimeSpan ExpiringWindow = TimeSpan.FromDays(14);

    public static IReadOnlyList<MemoryReviewItem> Select(IEnumerable<MemoryRecord> memories, DateTimeOffset now)
    {
        var items = new List<MemoryReviewItem>();
        foreach (var memory in memories)
        {
            if (memory.ValidUntil is { } until && until <= now) continue;
            var reasons = new List<string>();
            if (memory.SourceType != "user" && memory.CreatedAt >= now - RecentWindow)
                reasons.Add(MemoryReviewReasons.Recent);
            // A pinned memory was deliberately kept, so its confidence is not a question.
            if (memory.Confidence < LowConfidenceBelow && !memory.IsPinned)
                reasons.Add(MemoryReviewReasons.LowConfidence);
            if (memory.ValidUntil is { } expires && expires <= now + ExpiringWindow)
                reasons.Add(MemoryReviewReasons.Expiring);
            if (reasons.Count > 0) items.Add(new MemoryReviewItem(memory, reasons));
        }

        return items
            .OrderByDescending(item => item.Reasons.Count)
            .ThenByDescending(item => item.Memory.CreatedAt)
            .ThenBy(item => item.Memory.Id)
            .Take(MaxItems)
            .ToArray();
    }
}

/// <summary>Where a memory came from, in a form the app can show and open.</summary>
public sealed record MemorySourceView(string Type, string Label, Guid? ConversationId, Guid? MessageId);

public sealed record MemoryReviewEntry(
    Guid Id,
    string Kind,
    string Content,
    float Confidence,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidUntil,
    IReadOnlyList<string> Reasons,
    MemorySourceView Source,
    Guid? ProfileId,
    string? ProfileName,
    int Corrections);

public sealed record MemoryVersionView(Guid Id, string Content, DateTimeOffset CreatedAt, DateTimeOffset? ReplacedAt);

public enum MemoryCorrectionOutcome
{
    Corrected,
    NotFound,

    /// <summary>Pinned memories are changed by editing or unpinning them, never replaced behind the owner's back.</summary>
    Pinned,
    NotCurrent,
    Unchanged
}

public sealed record MemoryCorrectionResult(MemoryCorrectionOutcome Outcome, MemoryRecord? Memory = null);

public interface IMemoryReviewService
{
    Task<IReadOnlyList<MemoryReviewEntry>> GetReviewAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a current memory with the corrected text. The old one stays as history, no longer current, and its
    /// graph facts end with it, so later answers use only the correction.
    /// </summary>
    /// <exception cref="ArgumentException">The text is empty or too long.</exception>
    Task<MemoryCorrectionResult> CorrectAsync(Guid ownerId, Guid memoryId, string content,
        CancellationToken cancellationToken);

    /// <summary>Ends a current memory without a replacement ("that is no longer true").</summary>
    Task<MemoryCorrectionResult> ExpireAsync(Guid ownerId, Guid memoryId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryVersionView>> HistoryAsync(Guid ownerId, Guid memoryId,
        CancellationToken cancellationToken);
}
