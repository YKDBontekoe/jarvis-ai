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
    TimeProvider? clock = null) : IHomeContextService
{
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(3);

    public async Task<HomeContextDto> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        // Sources run one after another: they share the request's database context, which is not thread safe.
        var preference = await Read("briefing settings", () => briefing.GetAsync(ownerId, cancellationToken), null);
        var zone = Zone(preference?.TimeZoneId);
        var reminderList = await Read("reminders", () => reminders.ListAsync(ownerId, cancellationToken), []);
        var taskList = await Read("tasks", () => tasks.ListAsync(ownerId, cancellationToken), []);
        var events = await Read("calendar",
            () => calendar.ListUpcomingAsync(ownerId, now.AddHours(-1), now.AddHours(48), cancellationToken), []);
        var device = await Read("device", () => telemetry.GetAsync(ownerId, cancellationToken), null);
        var notes = await Read("notifications", () => notifications.ListNotificationsAsync(ownerId, cancellationToken), []);
        var everyone = await Read("people", () => people.ListAsync(ownerId, cancellationToken), []);
        var chatLinks = await Read("people links", () => links.ListAsync(ownerId, cancellationToken), []);
        var overview = await Read<RadarOverview?>("radar", async () => await radar.OverviewAsync(ownerId, cancellationToken), null);
        var suggestions = await Read("routines", () => routines.ListAsync(ownerId, cancellationToken), []);
        var dismissed = await Read("dismissals",
            () => settings.GetAsync<HomeContextDismissals>(ownerId, HomeContextDismissals.Section, cancellationToken),
            null);

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

    private async Task<T> Read<T>(string source, Func<Task<T>> load, T fallback)
    {
        try
        {
            return await load().WaitAsync(SourceTimeout);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Home left out {Source}.", source);
            return fallback;
        }
    }
}
