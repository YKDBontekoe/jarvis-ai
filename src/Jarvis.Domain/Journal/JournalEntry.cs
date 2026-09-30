namespace Jarvis.Domain.Journal;

/// <summary>
/// One journal entry about a single day. Ratings are optional: <see cref="Rating"/> is 1-10, while
/// <see cref="Mood"/>, <see cref="Energy"/>, and <see cref="Stress"/> are 1-5. <see cref="MemoryId"/> links the
/// searchable memory that mirrors the entry so Jarvis can recall it.
/// </summary>
public sealed record JournalEntry(
    Guid Id,
    Guid OwnerId,
    DateOnly EntryDate,
    string Source,
    string Content,
    string? Highlights,
    string? Gratitude,
    int? Rating,
    int? Mood,
    int? Energy,
    int? Stress,
    IReadOnlyList<string> Tags,
    Guid? MemoryId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
