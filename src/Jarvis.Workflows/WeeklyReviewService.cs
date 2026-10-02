using Jarvis.Application.Reviews;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Microsoft.Extensions.Logging;

namespace Jarvis.Workflows;

public sealed class WeeklyReviewService(
    IOwnerSettingsStore settings,
    IWeeklyReviewRepository reviews,
    IWeeklyReviewNarrator narrator,
    IWeeklyReviewScheduler scheduler,
    IDailyBriefingRepository briefings,
    TimeProvider clock,
    ILogger<WeeklyReviewService> logger) : IWeeklyReviewService
{
    public const int DefaultTrendWeeks = 8;
    public const int MaxTrendWeeks = 26;

    public async Task<WeeklyReviewOverview> GetOverviewAsync(Guid ownerId, int weeks,
        CancellationToken cancellationToken)
    {
        var current = await LoadSettingsAsync(ownerId, cancellationToken);
        var zone = Zone(current.TimeZoneId);
        var now = clock.GetUtcNow();
        var weekStart = WeeklyReviewClock.CurrentWeekStart(now, zone);
        var trend = await reviews.TrendAsync(ownerId, weekStart, Math.Clamp(weeks, 4, MaxTrendWeeks),
            cancellationToken);
        var list = await reviews.ListAsync(ownerId, 12, cancellationToken);
        DateTimeOffset? next = null;
        if (current.Enabled)
        {
            var lastNotified = await reviews.LastNotifiedWeekAsync(ownerId, cancellationToken);
            next = WeeklyReviewClock.ResolveNext(now, current.LocalTime, zone, lastNotified).FireAt;
        }

        return new WeeklyReviewOverview(current, weekStart, next, trend, list);
    }

    public Task<WeeklyReviewRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        reviews.GetAsync(ownerId, id, cancellationToken);

    public async Task<WeeklyReviewSettings> SaveSettingsAsync(Guid ownerId, WeeklyReviewSettings request,
        CancellationToken cancellationToken)
    {
        var normalized = request.Normalize();
        await settings.SaveAsync(ownerId, SettingsSections.WeeklyReview, normalized, cancellationToken);
        if (normalized.Enabled)
        {
            try
            {
                await scheduler.ScheduleWeeklyReviewAsync(ownerId, settingsChanged: true, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogInformation(exception,
                    "Weekly review scheduling for {OwnerId} will be repaired by the worker reconciler.", ownerId);
            }
        }

        return normalized;
    }

    public async Task<WeeklyReviewRecord> GenerateNowAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var current = await LoadSettingsAsync(ownerId, cancellationToken);
        var weekStart = WeeklyReviewClock.CurrentWeekStart(clock.GetUtcNow(), Zone(current.TimeZoneId));
        return (await WriteAsync(ownerId, weekStart, current.TimeZoneId, notify: false, cancellationToken))!;
    }

    public async Task<WeeklyReviewSchedule> ResolveScheduleAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync<WeeklyReviewSettings>(ownerId, SettingsSections.WeeklyReview,
            cancellationToken);
        if (current is null || !current.Enabled)
            return new WeeklyReviewSchedule(false, clock.GetUtcNow(), default);
        var lastNotified = await reviews.LastNotifiedWeekAsync(ownerId, cancellationToken);
        var (fireAt, weekStart) = WeeklyReviewClock.ResolveNext(clock.GetUtcNow(), current.LocalTime,
            Zone(current.TimeZoneId), lastNotified);
        return new WeeklyReviewSchedule(true, fireAt, weekStart);
    }

    public async Task<bool> DeliverAsync(WeeklyReviewActivityInput input, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync<WeeklyReviewSettings>(input.OwnerId, SettingsSections.WeeklyReview,
            cancellationToken);
        if (current is null || !current.Enabled) return false;
        if (await reviews.IsNotifiedAsync(input.OwnerId, input.WeekStart, cancellationToken)) return true;
        await WriteAsync(input.OwnerId, input.WeekStart, current.TimeZoneId, notify: true, cancellationToken);
        return true;
    }

    private async Task<WeeklyReviewRecord?> WriteAsync(Guid ownerId, DateOnly weekStart, string timeZoneId,
        bool notify, CancellationToken cancellationToken)
    {
        if (!LocalClock.TryFind(timeZoneId, out _)) timeZoneId = "UTC";
        var facts = await reviews.CollectAsync(ownerId, weekStart, timeZoneId, cancellationToken);
        string? narration = null;
        try
        {
            narration = await narrator.NarrateAsync(ownerId, facts, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Weekly review narration failed for {OwnerId}; using the plain summary.",
                ownerId);
        }

        var narrated = !string.IsNullOrWhiteSpace(narration);
        var story = narrated ? narration!.Trim() : WeeklyReviewComposer.Compose(facts);
        return await reviews.SaveAsync(ownerId, facts, story, narrated, notify, cancellationToken);
    }

    private async Task<WeeklyReviewSettings> LoadSettingsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync<WeeklyReviewSettings>(ownerId, SettingsSections.WeeklyReview,
            cancellationToken);
        if (stored is not null) return stored;
        var zone = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        return WeeklyReviewSettings.Default with
        {
            TimeZoneId = LocalClock.TryFind(zone, out _) && !string.IsNullOrWhiteSpace(zone) ? zone! : "UTC"
        };
    }

    private static TimeZoneInfo Zone(string? timeZoneId) =>
        LocalClock.TryFind(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
}
