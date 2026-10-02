namespace Jarvis.Domain.Habits;

/// <summary>
/// A habit the owner wants to keep, such as "Exercise" or "Read 20 pages". <see cref="Cadence"/> is "daily" or
/// "weekly"; a weekly habit counts as kept in a week once it has <see cref="TargetPerWeek"/> check-ins (Monday to
/// Sunday). Archived habits keep their history but drop out of streaks, check-ins, and the evening question.
/// </summary>
public sealed record Habit(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Icon,
    string Cadence,
    int TargetPerWeek,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsArchived => ArchivedAt is not null;
}

/// <summary>One day on which the owner did a <see cref="Habit"/>. There is at most one check-in per habit per day.</summary>
public sealed record HabitCheckIn(
    Guid Id,
    Guid HabitId,
    Guid OwnerId,
    DateOnly Date,
    string Source,
    DateTimeOffset CreatedAt);
