using System.Text.Json;
using Jarvis.Application.Decisions;
using Jarvis.Application.Reviews;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class WeeklyReviewRepository(JarvisDbContext db) : IWeeklyReviewRepository
{
    public const string NotificationType = "briefing.weekly";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WeeklyReviewFacts> CollectAsync(Guid ownerId, DateOnly weekStart, string timeZoneId,
        CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var (start, end) = WeeklyReviewClock.Window(weekStart, zone);
        var weekEnd = weekStart.AddDays(7);
        var previousStart = weekStart.AddDays(-7);

        var journal = await db.JournalEntries.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.EntryDate >= previousStart && x.EntryDate < weekEnd)
            .OrderBy(x => x.EntryDate).ThenBy(x => x.CreatedAt)
            .Select(x => new { x.EntryDate, x.Mood, x.Energy, x.Stress, x.Rating, x.Tags, x.Highlights })
            .ToListAsync(cancellationToken);
        var thisWeek = journal.Where(x => x.EntryDate >= weekStart).ToArray();
        var previousMoods = journal.Where(x => x.EntryDate < weekStart && x.Mood is not null)
            .Select(x => (double)x.Mood!.Value).ToArray();

        var completedTasks = await db.Tasks.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.Status == "completed" &&
                x.CompletedAt >= start && x.CompletedAt < end)
            .OrderBy(x => x.CompletedAt)
            .Select(x => x.Title)
            .ToListAsync(cancellationToken);
        var remindersHandled = await db.Reminders.AsNoTracking()
            .CountAsync(x => x.OwnerId == ownerId &&
                ((x.Status == "completed" && x.CompletedAt >= start && x.CompletedAt < end) ||
                 (x.Recurrence != Reminder.RecurrenceNone && x.LastDeliveredAt >= start && x.LastDeliveredAt < end)),
                cancellationToken);
        var nextWeekEnd = end.AddDays(7);
        var remindersUpcoming = await db.Reminders.AsNoTracking()
            .CountAsync(x => x.OwnerId == ownerId && x.Status == "pending" && x.DueAt >= end && x.DueAt < nextWeekEnd,
                cancellationToken);
        var memoryQuery = db.Memories.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.CreatedAt >= start && x.CreatedAt < end &&
                (x.SourceType == null || x.SourceType != "journal"));
        var newMemories = await memoryQuery.CountAsync(cancellationToken);
        var memorySnippets = await memoryQuery
            .OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.Importance).ThenBy(x => x.CreatedAt)
            .Take(5).Select(x => x.Content).ToListAsync(cancellationToken);

        var samples = thisWeek
            .Select(x => new JournalSample(x.EntryDate, x.Mood, x.Energy, x.Stress, x.Rating, x.Tags))
            .ToArray();

        // Calibration: the decisions settled this week, against the latest ones settled before it.
        var settled = (await db.Decisions.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Outcome != null && x.ResolvedAt >= start && x.ResolvedAt < end)
                .Select(x => new { x.Probability, x.Outcome, x.ResolvedAt }).ToListAsync(cancellationToken))
            .Select(x => new ResolvedPrediction(x.Probability, x.Outcome!.Value, x.ResolvedAt!.Value)).ToArray();
        var earlier = (await db.Decisions.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Outcome != null && x.ResolvedAt < start)
                .OrderByDescending(x => x.ResolvedAt).Take(CalibrationCalculator.TrendWindow * 2)
                .Select(x => new { x.Probability, x.Outcome, x.ResolvedAt }).ToListAsync(cancellationToken))
            .Select(x => new ResolvedPrediction(x.Probability, x.Outcome!.Value, x.ResolvedAt!.Value)).ToArray();

        var stats = WeeklyReviewComposer.BuildStats(samples,
            previousMoods.Length == 0 ? null : Math.Round(previousMoods.Average(), 2),
            completedTasks.Count, remindersHandled, remindersUpcoming, newMemories,
            settled.Length, CalibrationCalculator.Brier(settled), CalibrationCalculator.Brier(earlier));
        return new WeeklyReviewFacts(weekStart, timeZoneId, stats,
            thisWeek.Select(x => x.Highlights).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Clip(x!, 200)).Take(7).ToArray(),
            completedTasks.Select(x => Clip(x, 120)).Take(8).ToArray(),
            memorySnippets.Select(x => Clip(x, 140)).ToArray());
    }

    public async Task<IReadOnlyList<WeeklyTrendPoint>> TrendAsync(Guid ownerId, DateOnly lastWeekStart, int weeks,
        CancellationToken cancellationToken)
    {
        var from = lastWeekStart.AddDays(-7 * (weeks - 1));
        var to = lastWeekStart.AddDays(7);
        var samples = await db.JournalEntries.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.EntryDate >= from && x.EntryDate < to)
            .Select(x => new { x.EntryDate, x.Mood, x.Energy, x.Stress, x.Rating })
            .ToListAsync(cancellationToken);
        return WeeklyReviewComposer.Trend(
            samples.Select(x => new JournalSample(x.EntryDate, x.Mood, x.Energy, x.Stress, x.Rating, [])),
            lastWeekStart, weeks);
    }

    public async Task<IReadOnlyList<WeeklyReviewRecord>> ListAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken) =>
        (await db.WeeklyReviews.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.WeekStart).Take(Math.Clamp(limit, 1, 52))
            .ToListAsync(cancellationToken))
        .Select(ToRecord).ToArray();

    public async Task<WeeklyReviewRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var review = await db.WeeklyReviews.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id, cancellationToken);
        return review is null ? null : ToRecord(review);
    }

    public async Task<DateOnly?> LastNotifiedWeekAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.WeeklyReviews.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.NotifiedAt != null)
            .OrderByDescending(x => x.WeekStart)
            .Select(x => (DateOnly?)x.WeekStart)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> IsNotifiedAsync(Guid ownerId, DateOnly weekStart, CancellationToken cancellationToken) =>
        db.WeeklyReviews.AsNoTracking()
            .AnyAsync(x => x.OwnerId == ownerId && x.WeekStart == weekStart && x.NotifiedAt != null, cancellationToken);

    public async Task<WeeklyReviewRecord?> SaveAsync(Guid ownerId, WeeklyReviewFacts facts, string story,
        bool narrated, bool notify, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var review = await db.WeeklyReviews
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.WeekStart == facts.WeekStart, cancellationToken);
        if (notify && review?.NotifiedAt is not null) return null;

        var statsJson = JsonSerializer.Serialize(facts.Stats, JsonOptions);
        if (review is null)
        {
            review = new WeeklyReview(ownerId, facts.WeekStart, facts.TimeZoneId, story, narrated, statsJson);
            db.WeeklyReviews.Add(review);
        }
        else
        {
            review.Update(facts.TimeZoneId, story, narrated, statsJson);
        }

        var metadata = JsonSerializer.Serialize(new { weekStart = facts.WeekStart, narrated });
        if (notify)
        {
            review.MarkNotified();
            var notification = new Notification(Guid.CreateVersion7(), ownerId, NotificationType,
                "Your week in review", WeeklyReviewComposer.NotificationBody(story), review.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
            db.AuditEvents.Add(new AuditEvent(ownerId, "briefings", "review.weekly_delivered", "low", true,
                metadataJson: metadata));
        }
        else
        {
            db.AuditEvents.Add(new AuditEvent(ownerId, "briefings", "review.weekly_generated", "low", true,
                metadataJson: metadata));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToRecord(review);
    }

    private static WeeklyReviewRecord ToRecord(WeeklyReview review) =>
        new(review.Id, review.WeekStart, review.WeekStart.AddDays(6), review.Story, review.Narrated,
            JsonSerializer.Deserialize<WeeklyReviewStats>(review.StatsJson, JsonOptions) ?? EmptyStats,
            review.CreatedAt, review.UpdatedAt, review.NotifiedAt);

    private static readonly WeeklyReviewStats EmptyStats =
        new(0, null, null, null, null, null, null, null, 0, 0, 0, 0, [], []);

    private static string Clip(string value, int max)
    {
        var text = value.ReplaceLineEndings(" ").Trim();
        return text.Length > max ? text[..(max - 1)] + "…" : text;
    }
}
