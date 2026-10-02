namespace Jarvis.Domain.People;

/// <summary>
/// Someone in the owner's life. The birthday is stored as month and day so it works without a known year;
/// <see cref="BirthYear"/> is optional and only used to show an age. <see cref="ContactEveryDays"/> is how often the
/// owner wants to be in touch ("call mum every 2 weeks"). <see cref="GraphEntityId"/> links the knowledge-graph
/// entity Jarvis learned about this person, when there is one. <see cref="BirthdayNotifiedYear"/> and
/// <see cref="CheckInNudgedAt"/> keep the daily check-in from notifying twice.
/// </summary>
public sealed record Person(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Relationship,
    int? BirthdayMonth,
    int? BirthdayDay,
    int? BirthYear,
    string? Notes,
    int? ContactEveryDays,
    DateTimeOffset? LastContactedAt,
    Guid? GraphEntityId,
    int? BirthdayNotifiedYear,
    DateTimeOffset? CheckInNudgedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool HasBirthday => BirthdayMonth is not null && BirthdayDay is not null;

    /// <summary>True when the daily check-in has something to watch for this person.</summary>
    public bool NeedsCheckIns => HasBirthday || ContactEveryDays is not null;
}
