namespace Jarvis.Domain.Workflows;

/// <summary>
/// One owner's look back at a Monday-to-Sunday week in their own time zone. <see cref="StatsJson"/> holds the
/// numbers the story was written from, so trends stay stable even after journal entries are edited later.
/// <see cref="NotifiedAt"/> is set only by the scheduled Sunday delivery; a review made on demand stays unnotified.
/// </summary>
public sealed class WeeklyReview
{
    public const int MaxStoryLength = 4_000;

    private WeeklyReview() { }

    public WeeklyReview(Guid ownerId, DateOnly weekStart, string timeZoneId, string story, bool narrated,
        string statsJson)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
            throw new ArgumentException("A week review starts on a Monday.", nameof(weekStart));
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        WeekStart = weekStart;
        CreatedAt = DateTimeOffset.UtcNow;
        Update(timeZoneId, story, narrated, statsJson);
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public DateOnly WeekStart { get; private set; }
    public string TimeZoneId { get; private set; } = "UTC";
    public string Story { get; private set; } = string.Empty;
    public bool Narrated { get; private set; }
    public string StatsJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? NotifiedAt { get; private set; }

    public void Update(string timeZoneId, string story, bool narrated, string statsJson)
    {
        TimeZoneId = timeZoneId;
        Story = story.Length > MaxStoryLength ? story[..(MaxStoryLength - 1)] + "…" : story;
        Narrated = narrated;
        StatsJson = statsJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNotified() => NotifiedAt ??= DateTimeOffset.UtcNow;
}
