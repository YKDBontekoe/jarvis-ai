using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Planner;

/// <summary>
/// Builds the Today timeline from the calendar feed, reminders and the owner's day plan, and places open to-dos in
/// free time. The calendar is only read here; adding blocks to it goes through the approval-gated calendar tool.
/// </summary>
public sealed class DayPlannerService(
    IOwnerSettingsStore settings,
    ICalendarFeed calendar,
    IIntegrationCredentialStore credentials,
    IReminderService reminders,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IDayPlannerService
{
    public const string Section = "planner.day";
    public const int MaxItems = 30;
    public const int MaxTitleLength = 200;
    public const int MinMinutes = 5;
    public const int MaxMinutes = 480;
    public const int DefaultMinutes = 30;
    public static readonly TimeOnly DefaultDayStart = new(8, 0);
    public static readonly TimeOnly DefaultDayEnd = new(18, 0);

    /// <summary>Events at least this long are all-day: shown, but they do not block time.</summary>
    private static readonly TimeSpan AllDay = TimeSpan.FromHours(20);

    private static readonly TimeSpan DefaultEventLength = TimeSpan.FromMinutes(30);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DayTimelineDto> GetTodayAsync(Guid ownerId, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        if (day.Changed) await SaveAsync(ownerId, day.State, cancellationToken);
        return await BuildAsync(ownerId, day, cancellationToken);
    }

    public async Task<DayPlanItemDto> AddItemAsync(Guid ownerId, AddDayPlanItemRequest request, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var title = CleanTitle(request.Title);
        var minutes = CleanMinutes(request.Minutes ?? DefaultMinutes);
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        if (day.State.Items.Count >= MaxItems)
            throw new ArgumentException($"A day plan holds at most {MaxItems} to-dos. Finish or remove one first.");
        var item = new DayPlanItem(Guid.NewGuid(), title, minutes, false, null, clock.GetUtcNow());
        await SaveAsync(ownerId, day.State with { Items = [.. day.State.Items, item] }, cancellationToken);
        return ToDto(item);
    }

    public async Task<DayPlanItemDto?> UpdateItemAsync(Guid ownerId, Guid itemId, UpdateDayPlanItemRequest request,
        string? timeZoneId, CancellationToken cancellationToken)
    {
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        var current = day.State.Items.FirstOrDefault(item => item.Id == itemId);
        if (current is null) return null;
        var updated = current with
        {
            Title = request.Title is null ? current.Title : CleanTitle(request.Title),
            Minutes = request.Minutes is int minutes ? CleanMinutes(minutes) : current.Minutes,
            Done = request.Done ?? current.Done
        };
        // A new length no longer matches the block it had, so it waits for the next plan.
        if (updated.Minutes != current.Minutes && !updated.Done) updated = updated with { StartAt = null };
        await SaveAsync(ownerId, day.State with
        {
            Items = day.State.Items.Select(item => item.Id == itemId ? updated : item).ToArray()
        }, cancellationToken);
        return ToDto(updated);
    }

    public async Task<bool> RemoveItemAsync(Guid ownerId, Guid itemId, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        if (day.State.Items.All(item => item.Id != itemId)) return false;
        await SaveAsync(ownerId, day.State with
        {
            Items = day.State.Items.Where(item => item.Id != itemId).ToArray()
        }, cancellationToken);
        return true;
    }

    public async Task<DayPlanResultDto> PlanAsync(Guid ownerId, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        var events = await ReadEventsAsync(ownerId, day, cancellationToken);
        var now = clock.GetUtcNow();
        var window = Window(day, now);

        // Done to-dos keep their block; open ones are placed again, so a missed block moves to later today.
        var busy = BlockingEvents(events.Events)
            .Concat(day.State.Items.Where(item => item.Done && item.StartAt is not null)
                .Select(item => new TimeRange(item.StartAt!.Value, item.StartAt.Value.AddMinutes(item.Minutes))))
            .ToArray();
        var open = day.State.Items.Where(item => !item.Done).ToArray();
        var placed = DayPlanScheduler.Place(window, busy,
            open.Select(item => (item.Id, TimeSpan.FromMinutes(item.Minutes))));

        var items = day.State.Items.Select(item => item.Done
                ? item
                : item with { StartAt = placed.TryGetValue(item.Id, out var start) ? start : null })
            .ToArray();
        var state = day.State with { Items = items, PlannedAt = now };
        await SaveAsync(ownerId, state, cancellationToken);
        var today = Build(day with { State = state }, events, await TodayRemindersAsync(ownerId, day,
            cancellationToken), now);
        var missed = items.Where(item => !item.Done && item.StartAt is null).Select(ToDto).ToArray();
        return new DayPlanResultDto(today, placed.Count, missed);
    }

    public async Task<DayTimelineDto> ClearPlanAsync(Guid ownerId, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        var state = day.State with
        {
            Items = day.State.Items.Select(item => item.Done ? item : item with { StartAt = null }).ToArray(),
            PlannedAt = null
        };
        await SaveAsync(ownerId, state, cancellationToken);
        return await BuildAsync(ownerId, day with { State = state }, cancellationToken);
    }

    public async Task<DayTimelineDto> SaveHoursAsync(Guid ownerId, SaveDayHoursRequest request, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        if (request.DayEnd <= request.DayStart || request.DayEnd - request.DayStart < TimeSpan.FromHours(1))
            throw new ArgumentException("The day must end at least an hour after it starts.");
        var day = await LoadDayAsync(ownerId, timeZoneId, cancellationToken);
        var state = day.State with { DayStart = request.DayStart, DayEnd = request.DayEnd };
        await SaveAsync(ownerId, state, cancellationToken);
        return await BuildAsync(ownerId, day with { State = state }, cancellationToken);
    }

    private async Task<DayTimelineDto> BuildAsync(Guid ownerId, Day day, CancellationToken cancellationToken)
    {
        var events = await ReadEventsAsync(ownerId, day, cancellationToken);
        var todayReminders = await TodayRemindersAsync(ownerId, day, cancellationToken);
        return Build(day, events, todayReminders, clock.GetUtcNow());
    }

    private static DayTimelineDto Build(Day day, CalendarRead events, IReadOnlyList<ReminderRecord> dayReminders,
        DateTimeOffset now)
    {
        var entries = new List<DayTimelineEntryDto>();
        entries.AddRange(events.Events.Select(item => new DayTimelineEntryDto(DayTimelineKinds.Event, null,
            item.Title, item.StartAt, item.EndAt, (item.EndAt ?? item.StartAt + DefaultEventLength) <= now)));
        entries.AddRange(dayReminders.Select(item => new DayTimelineEntryDto(DayTimelineKinds.Reminder,
            item.Id.ToString(), item.Title, item.DueAt, null, item.Status != "pending")));
        entries.AddRange(day.State.Items.Where(item => item.StartAt is not null).Select(item =>
            new DayTimelineEntryDto(DayTimelineKinds.Focus, item.Id.ToString(), item.Title, item.StartAt!.Value,
                item.StartAt.Value.AddMinutes(item.Minutes), item.Done)));

        var busy = BlockingEvents(events.Events).Concat(day.State.Items
            .Where(item => item.StartAt is not null)
            .Select(item => new TimeRange(item.StartAt!.Value, item.StartAt.Value.AddMinutes(item.Minutes))));
        var free = DayPlanScheduler.FreeSlots(Window(day, now), busy);
        return new DayTimelineDto(
            day.State.Date,
            day.ZoneId,
            day.State.DayStart,
            day.State.DayEnd,
            events.Connected,
            events.Unavailable,
            entries.OrderBy(entry => IsAllDay(entry.StartAt, entry.EndAt) ? 0 : 1)
                .ThenBy(entry => entry.StartAt)
                .ThenBy(entry => entry.Kind, StringComparer.Ordinal)
                .ToArray(),
            day.State.Items.OrderBy(item => item.Done)
                .ThenBy(item => item.StartAt ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.CreatedAt)
                .Select(ToDto)
                .ToArray(),
            free.Select(slot => new DayFreeSlotDto(slot.Start, slot.End, (int)slot.Length.TotalMinutes)).ToArray(),
            (int)free.Sum(slot => slot.Length.TotalMinutes),
            day.State.PlannedAt);
    }

    private static TimeRange Window(Day day, DateTimeOffset now)
    {
        var start = LocalClock.Resolve(day.State.Date.ToDateTime(day.State.DayStart), day.Zone);
        var end = LocalClock.Resolve(day.State.Date.ToDateTime(day.State.DayEnd), day.Zone);
        return new TimeRange(now > start ? now : start, end);
    }

    private static IEnumerable<TimeRange> BlockingEvents(IEnumerable<CalendarEventRecord> events) =>
        events.Where(item => !IsAllDay(item.StartAt, item.EndAt))
            .Select(item => new TimeRange(item.StartAt,
                item.EndAt is { } end && end > item.StartAt ? end : item.StartAt + DefaultEventLength));

    private static bool IsAllDay(DateTimeOffset start, DateTimeOffset? end) =>
        end is { } until && until - start >= AllDay;

    private async Task<CalendarRead> ReadEventsAsync(Guid ownerId, Day day, CancellationToken cancellationToken)
    {
        var connected = await credentials.GetStatusAsync(ownerId,
            IntegrationPackIds.Provider(IntegrationPackIds.Calendar), cancellationToken) is not null;
        if (!connected) return new CalendarRead([], false, false);
        var dayStart = LocalClock.Resolve(day.State.Date.ToDateTime(TimeOnly.MinValue), day.Zone);
        var dayEnd = LocalClock.Resolve(day.State.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), day.Zone);
        try
        {
            // All-day events sit at UTC midnight, which can be up to 14 hours before the local day starts.
            var events = await calendar.ListUpcomingAsync(ownerId, dayStart.AddHours(-14), dayEnd, cancellationToken);
            var today = events.Where(item => IsAllDay(item.StartAt, item.EndAt)
                    ? DateOnly.FromDateTime(item.StartAt.UtcDateTime) == day.State.Date
                    : item.StartAt < dayEnd && (item.EndAt ?? item.StartAt + DefaultEventLength) > dayStart)
                .ToArray();
            return new CalendarRead(today, true, false);
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException
                                              or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new CalendarRead([], true, true);
        }
    }

    private async Task<IReadOnlyList<ReminderRecord>> TodayRemindersAsync(Guid ownerId, Day day,
        CancellationToken cancellationToken)
    {
        var dayStart = LocalClock.Resolve(day.State.Date.ToDateTime(TimeOnly.MinValue), day.Zone);
        var dayEnd = LocalClock.Resolve(day.State.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), day.Zone);
        return (await reminders.ListAsync(ownerId, cancellationToken))
            .Where(item => item.Status != "cancelled" && item.DueAt >= dayStart && item.DueAt < dayEnd)
            .OrderBy(item => item.DueAt)
            .Take(20)
            .ToArray();
    }

    private async Task<Day> LoadDayAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken)
    {
        var saved = await settings.GetAsync<DayPlanState>(ownerId, Section, cancellationToken);
        // The device zone wins; then the zone the app last sent; then the briefing zone; then UTC.
        var explicitZone = !string.IsNullOrWhiteSpace(timeZoneId) && LocalClock.TryFind(timeZoneId, out _)
            ? timeZoneId.Trim()
            : null;
        var zoneId = explicitZone ?? saved?.TimeZoneId;
        if (string.IsNullOrWhiteSpace(zoneId) || !LocalClock.TryFind(zoneId, out _))
            zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        if (string.IsNullOrWhiteSpace(zoneId) || !LocalClock.TryFind(zoneId, out _)) zoneId = "UTC";
        LocalClock.TryFind(zoneId, out var zone);
        zoneId = zoneId.Trim();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var state = saved switch
        {
            null => new DayPlanState(today, [], DefaultDayStart, DefaultDayEnd, null),
            _ when saved.Date == today => saved,
            // A new day: open to-dos carry over without their old blocks; finished ones are cleared.
            _ => saved with
            {
                Date = today,
                Items = (saved.Items ?? []).Where(item => !item.Done).Select(item => item with { StartAt = null })
                    .ToArray(),
                PlannedAt = null
            }
        };
        state = state with { Items = state.Items ?? [] };
        var zoneChanged = explicitZone is not null && !string.Equals(explicitZone, state.TimeZoneId,
            StringComparison.Ordinal);
        if (zoneChanged) state = state with { TimeZoneId = explicitZone };
        return new Day(state, zone, zoneId, zoneChanged || (saved is not null && saved.Date != today));
    }

    private Task SaveAsync(Guid ownerId, DayPlanState state, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, Section, state, cancellationToken);

    private static DayPlanItemDto ToDto(DayPlanItem item) =>
        new(item.Id, item.Title, item.Minutes, item.Done, item.StartAt, item.StartAt?.AddMinutes(item.Minutes));

    private static string CleanTitle(string? title)
    {
        var clean = string.Join(' ', (title ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) throw new ArgumentException("Give the to-do a title.");
        if (clean.Length > MaxTitleLength)
            throw new ArgumentException($"Keep the title under {MaxTitleLength} characters.");
        return clean;
    }

    private static int CleanMinutes(int minutes)
    {
        if (minutes is < MinMinutes or > MaxMinutes)
            throw new ArgumentException($"A to-do takes between {MinMinutes} and {MaxMinutes} minutes.");
        return minutes;
    }

    /// <param name="Changed">True when loading rolled the day over or learned a new zone, so a read should save.</param>
    private sealed record Day(DayPlanState State, TimeZoneInfo Zone, string ZoneId, bool Changed);

    private sealed record CalendarRead(IReadOnlyList<CalendarEventRecord> Events, bool Connected, bool Unavailable);
}
