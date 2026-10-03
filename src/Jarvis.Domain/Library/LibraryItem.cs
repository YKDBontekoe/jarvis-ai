namespace Jarvis.Domain.Library;

/// <summary>
/// Something the owner wants to keep and find again: a clipped web page, a note, or a research report.
/// <see cref="Summary"/>, <see cref="KeyPoints"/> and <see cref="Tags"/> are written by a model or by rules.
/// <see cref="Content"/> is the text itself, which for a web page comes from outside and is untrusted.
/// </summary>
public sealed record LibraryItem(
    Guid Id,
    Guid OwnerId,
    string Kind,
    string? Url,
    string Title,
    string Summary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<string> Tags,
    string Content,
    string Origin,
    Guid? ProjectId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A question and answer to learn by spaced repetition (SM-2). <see cref="Ease"/> starts at 2.5;
/// <see cref="IntervalDays"/> is the gap until <see cref="DueOn"/> after the last good review.
/// </summary>
public sealed record Flashcard(
    Guid Id,
    Guid OwnerId,
    Guid? ItemId,
    string Front,
    string Back,
    double Ease,
    int IntervalDays,
    int Repetitions,
    int Lapses,
    DateOnly DueOn,
    DateOnly? LastReviewedOn,
    DateTimeOffset CreatedAt);
