using Jarvis.Application.Workflows;

namespace Jarvis.Application.Reviews;

/// <summary>When the Sunday look-back arrives. Stored in the owner settings section <c>weekly-review</c>.</summary>
public sealed record WeeklyReviewSettings(bool Enabled, TimeOnly LocalTime, string TimeZoneId)
{
    public static WeeklyReviewSettings Default { get; } = new(false, new TimeOnly(19, 0), "UTC");

    public WeeklyReviewSettings Normalize()
    {
        var zone = string.IsNullOrWhiteSpace(TimeZoneId) ? "UTC" : TimeZoneId.Trim();
        if (!LocalClock.TryFind(zone, out _))
            throw new ArgumentException("Time zone identifier is not recognized by this server.", nameof(TimeZoneId));
        return this with { TimeZoneId = zone, LocalTime = new TimeOnly(LocalTime.Hour, LocalTime.Minute) };
    }
}

/// <summary>One journaled day inside a week; values are null when the entry left them out.</summary>
public sealed record WeeklyReviewDay(DateOnly Date, int? Mood, int? Energy, int? Stress, int? Rating);

/// <summary>The numbers a weekly review is written from. Averages are null when no entry carried that value.</summary>
public sealed record WeeklyReviewStats(
    int JournalEntries,
    double? Mood,
    double? Energy,
    double? Stress,
    double? Rating,
    double? PreviousMood,
    DateOnly? BestDay,
    int? BestDayRating,
    int TasksCompleted,
    int RemindersHandled,
    int RemindersUpcoming,
    int NewMemories,
    IReadOnlyList<string> TopTags,
    IReadOnlyList<WeeklyReviewDay> Days);

/// <summary>
/// Everything the narrator may see about one week. Highlights, task titles and memory snippets are owner data and
/// untrusted text; they are passed to the model for the story but never stored with the review.
/// </summary>
public sealed record WeeklyReviewFacts(
    DateOnly WeekStart,
    string TimeZoneId,
    WeeklyReviewStats Stats,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> CompletedTasks,
    IReadOnlyList<string> NewMemories)
{
    public DateOnly WeekEnd => WeekStart.AddDays(6);
}

public sealed record WeeklyTrendPoint(DateOnly WeekStart, int Entries, double? Mood, double? Energy, double? Stress,
    double? Rating);

public sealed record WeeklyReviewRecord(
    Guid Id,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string Story,
    bool Narrated,
    WeeklyReviewStats Stats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? NotifiedAt);

public sealed record WeeklyReviewOverview(
    WeeklyReviewSettings Settings,
    DateOnly CurrentWeekStart,
    DateTimeOffset? NextDeliveryAt,
    IReadOnlyList<WeeklyTrendPoint> Trend,
    IReadOnlyList<WeeklyReviewRecord> Reviews);

public sealed record WeeklyReviewWorkflowInput(Guid OwnerId);

/// <summary>Next delivery for the weekly review workflow. <see cref="Continue"/> is false once the owner turned it off.</summary>
public sealed record WeeklyReviewSchedule(bool Continue, DateTimeOffset FireAt, DateOnly WeekStart);

public sealed record WeeklyReviewActivityInput(Guid OwnerId, DateOnly WeekStart);

public static class WeeklyReviewWorkflowIds
{
    public static string For(Guid ownerId) => $"jarvis-weekly-review-{ownerId:N}";
}

public interface IWeeklyReviewRepository
{
    Task<WeeklyReviewFacts> CollectAsync(Guid ownerId, DateOnly weekStart, string timeZoneId,
        CancellationToken cancellationToken);

    /// <summary>Journal averages per week, oldest first, ending with the week that starts on <paramref name="lastWeekStart"/>.</summary>
    Task<IReadOnlyList<WeeklyTrendPoint>> TrendAsync(Guid ownerId, DateOnly lastWeekStart, int weeks,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WeeklyReviewRecord>> ListAsync(Guid ownerId, int limit, CancellationToken cancellationToken);

    Task<WeeklyReviewRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>The latest week whose scheduled review was delivered.</summary>
    Task<DateOnly?> LastNotifiedWeekAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<bool> IsNotifiedAsync(Guid ownerId, DateOnly weekStart, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or refreshes the review for <see cref="WeeklyReviewFacts.WeekStart"/>. With <paramref name="notify"/>
    /// the push notification is queued in the same transaction; a week that was already delivered is left alone and
    /// null is returned.
    /// </summary>
    Task<WeeklyReviewRecord?> SaveAsync(Guid ownerId, WeeklyReviewFacts facts, string story, bool narrated, bool notify,
        CancellationToken cancellationToken);
}

public interface IWeeklyReviewNarrator
{
    Task<string?> NarrateAsync(Guid ownerId, WeeklyReviewFacts facts, CancellationToken cancellationToken);
}

public sealed class NoOpWeeklyReviewNarrator : IWeeklyReviewNarrator
{
    public Task<string?> NarrateAsync(Guid ownerId, WeeklyReviewFacts facts, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}

public interface IWeeklyReviewScheduler
{
    /// <summary>
    /// Starts the owner's weekly review workflow when it is not running. With <paramref name="settingsChanged"/> a
    /// running workflow is also woken so it picks up a new time or time zone straight away.
    /// </summary>
    Task ScheduleWeeklyReviewAsync(Guid ownerId, bool settingsChanged, CancellationToken cancellationToken);
}

public interface IWeeklyReviewService
{
    Task<WeeklyReviewOverview> GetOverviewAsync(Guid ownerId, int weeks, CancellationToken cancellationToken);
    Task<WeeklyReviewRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<WeeklyReviewSettings> SaveSettingsAsync(Guid ownerId, WeeklyReviewSettings request,
        CancellationToken cancellationToken);

    /// <summary>Writes (or rewrites) the review for the current week without sending a notification.</summary>
    Task<WeeklyReviewRecord> GenerateNowAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<WeeklyReviewSchedule> ResolveScheduleAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Scheduled Sunday delivery. Returns false when the owner turned the review off.</summary>
    Task<bool> DeliverAsync(WeeklyReviewActivityInput input, CancellationToken cancellationToken);
}
