using Jarvis.Application.Devices;
using Jarvis.Application.Home;
using Jarvis.Application.Integrations;
using Jarvis.Application.People;
using Jarvis.Application.People.Radar;
using Jarvis.Application.Routines;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.People;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Home;

/// <summary>
/// Gathers the owner's day, people and patterns for Home. Every source is read on its own with a short time limit,
/// so a slow or failing one only leaves its part of Home out.
/// </summary>
public sealed class HomeContextService(
    IReminderService reminders,
    IJarvisTaskService tasks,
    ICalendarFeed calendar,
    IDeviceTelemetryStore telemetry,
    IDailyBriefingService briefing,
    INotificationRepository notifications,
    IPeopleService people,
    IPersonLinkRepository links,
    IRelationshipRadarService radar,
    IRoutineSuggestionService routines,
    IOwnerSettingsStore settings,
    ILogger<HomeContextService> logger,
    TimeProvider? clock = null,
    TimeSpan? sourceTimeout = null) : IHomeContextService
{
    private static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SettleGrace = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _sourceTimeout = sourceTimeout ?? DefaultSourceTimeout;

    public async Task<HomeContextDto> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        // Sources run one after another: they share the request's database context, which is not thread safe.
        var preference = await Read("briefing settings", token => briefing.GetAsync(ownerId, token), null, cancellationToken);
        var zone = Zone(preference?.TimeZoneId);
        var reminderList = await Read("reminders", token => reminders.ListAsync(ownerId, token), [], cancellationToken);
        var taskList = await Read("tasks", token => tasks.ListAsync(ownerId, token), [], cancellationToken);
        var events = await Read("calendar",
            token => calendar.ListUpcomingAsync(ownerId, now.AddHours(-1), now.AddHours(48), token), [], cancellationToken);
        var device = await Read("device", token => telemetry.GetAsync(ownerId, token), null, cancellationToken);
        var notes = await Read("notifications", token => notifications.ListNotificationsAsync(ownerId, token), [], cancellationToken);
        var everyone = await Read("people", token => people.ListAsync(ownerId, token), [], cancellationToken);
        var chatLinks = await Read("people links", token => links.ListAsync(ownerId, token), [], cancellationToken);
        var overview = await Read<RadarOverview?>("radar", async token => await radar.OverviewAsync(ownerId, token), null, cancellationToken);
        var suggestions = await Read("routines", token => routines.ListAsync(ownerId, token), [], cancellationToken);
        var dismissed = await Read("dismissals",
            token => settings.GetAsync<HomeContextDismissals>(ownerId, HomeContextDismissals.Section, token),
            null, cancellationToken);

        var agenda = new List<HomeAgendaFact>();
        foreach (var reminder in reminderList)
        {
            if (reminder.Place is not null) continue;
            if (reminder.Status is "done" or "completed" or "cancelled") continue;
            agenda.Add(new HomeAgendaFact(reminder.Title, reminder.DueAt, "reminder", reminder.Id));
        }

        agenda.AddRange(events.Select(item => new HomeAgendaFact(item.Title, item.StartAt, "event")));
        var facts = new HomeContextFacts(now, zone, agenda,
            HomeBriefingService.PickLatestBriefing(notes, now), device?.BatteryPercent, device?.Charging,
            device?.ReportedAt);
        var personRows = HomePeople.Build(everyone, chatLinks, overview?.People ?? []);
        var done = reminderList
            .Where(item => item.CompletedAt is { } at && HeroSelectorDay(at, now, zone))
            .Select(item => item.Title)
            .ToList();
        var tasksDone = taskList.Count(item => item.Status == "completed" && item.CompletedAt is { } at &&
                                               HeroSelectorDay(at, now, zone));
        return new HomeContextDto(
            DayLine.Compose(facts),
            HeroSelector.Pick(facts, dismissed),
            personRows,
            InsightRules.Build(suggestions, personRows, everyone, now, zone, dismissed),
            HeroSelector.Tomorrow(facts).ToArray(),
            new HomeDayRecapDto(done.Count, tasksDone, done.Take(5).ToArray()));
    }

    public async Task<bool> DismissAsync(Guid ownerId, HomeContextDismissRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Id?.Trim();
        var kind = request.Kind?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 200 || kind is null) return false;
        if (!HomeHeroKinds.All.Contains(kind) && !HomeInsightKinds.All.Contains(kind)) return false;
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (kind == HomeInsightKinds.Routine && id.StartsWith("routine:", StringComparison.Ordinal) &&
            Guid.TryParse(id["routine:".Length..], out var routineId))
        {
            // A routine Jarvis noticed is declined where it lives, so it is not suggested again.
            await routines.DismissAsync(routineId, ownerId, cancellationToken);
        }

        var current = await settings.GetAsync<HomeContextDismissals>(ownerId, HomeContextDismissals.Section,
            cancellationToken) ?? new HomeContextDismissals();
        await settings.SaveAsync(ownerId, HomeContextDismissals.Section,
            current.With(id, kind, request.Mute, now), cancellationToken);
        return true;
    }

    private static bool HeroSelectorDay(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).Date == TimeZoneInfo.ConvertTime(now, zone).Date;

    private static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Reads one source with a time limit. The source is told to stop when the limit passes, and given a moment to
    /// do so: the next source runs on the same database context, which cannot serve two queries at once, so an
    /// abandoned query would make every later source fail too. Cancelling the request itself still propagates.
    /// </summary>
    private async Task<T> Read<T>(string source, Func<CancellationToken, Task<T>> load, T fallback,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_sourceTimeout);
        Task<T>? work = null;
        try
        {
            work = load(limit.Token);
            return await work.WaitAsync(limit.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Home left out {Source}.", source);
            if (work is { IsCompleted: false }) await SettleAsync(work);
            return fallback;
        }
    }

    private static async Task SettleAsync(Task work)
    {
        try
        {
            await work.WaitAsync(SettleGrace);
        }
        catch
        {
            // The source already failed or was cancelled; there is nothing more to do with it.
        }
    }
}
