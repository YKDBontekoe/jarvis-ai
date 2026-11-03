using System.Text.Json;
using Jarvis.Application.Goals;
using Jarvis.Application.Integrations;
using Jarvis.Application.Planner;

namespace Jarvis.Application.Proactivity;

/// <summary>One owner-wide week; calendar reads never create or move an external event.</summary>
public sealed class ProactiveCapacityService(IOwnerSettingDocuments documents, ICalendarFeed calendar,
    IIntegrationCredentialStore credentials, TimeProvider? timeProvider = null) : IProactiveCapacityService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProactiveCapacity> CalculateAsync(ProactiveCase value, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(value.Policy.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var week = GoalLogic.MondayOf(today);
        var scope = value.SourceRefs.Where(r => r.StartsWith("goal:", StringComparison.Ordinal)).ToHashSet();
        var rawGoals = await documents.ReadDocumentAsync(value.OwnerId, GoalState.Section, ct);
        var goals = rawGoals is null ? null : JsonSerializer.Deserialize<GoalState>(rawGoals, Json);
        var consumed = (goals?.Goals ?? []).Where(g => g.Plan?.WeekStart == week &&
                (scope.Count == 0 || scope.Contains($"goal:{g.Id}")))
            .Sum(g => g.Plan!.Items.Where(i => i.Status == GoalPlanStatuses.Done).Sum(i => i.Minutes));
        ProactiveCapacity Unknown(string reason) => new(week, null, null, consumed, reason, now);
        if (!value.Policy.UseCalendarCapacity)
            return new(week, value.AvailableMinutes, value.AvailableMinutes is { } supplied ? Math.Max(0, supplied - consumed) : null,
                consumed, value.AvailableMinutes is { } minutes ? $"Shared weekly budget: {minutes} minutes − {consumed} completed = {Math.Max(0, minutes - consumed)} remaining." :
                    "Weekly capacity is unknown. Supply a shared budget or explicitly include calendar availability.", now);
        if (value.ProfileId is not null) return Unknown("Calendar capacity is owner-wide. Choose no restricted profile.");
        if (await credentials.GetStatusAsync(value.OwnerId, IntegrationPackIds.Provider(IntegrationPackIds.Calendar), ct) is null)
            return Unknown("Calendar is not connected. Weekly calendar availability is unknown.");
        var rawDay = await documents.ReadDocumentAsync(value.OwnerId, "planner.day", ct);
        var day = rawDay is null ? null : JsonSerializer.Deserialize<DayPlanState>(rawDay, Json);
        if (day is null || day.DayEnd <= day.DayStart)
            return Unknown("Choose working hours in Today before using them as the weekday planning window.");
        IReadOnlyList<CalendarEventRecord> events;
        try
        {
            events = await calendar.ListUpcomingAsync(value.OwnerId,
                Workflows.LocalClock.Resolve(week.ToDateTime(TimeOnly.MinValue), zone),
                Workflows.LocalClock.Resolve(week.AddDays(7).ToDateTime(TimeOnly.MinValue), zone), ct);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or TimeoutException ||
                                      error is TaskCanceledException && !ct.IsCancellationRequested)
        { return Unknown("Calendar could not be checked. Availability is unknown; no goal was moved."); }
        if (events.Any(e => e.EndAt is null || e.EndAt <= e.StartAt))
            return Unknown("A calendar event has no reliable end time. Confirm its duration before allocating the week.");
        var busy = events.Select(e => new TimeRange(e.StartAt, e.EndAt!.Value.Add(DayPlanScheduler.Buffer))).ToArray();
        var free = 0;
        for (var index = 0; index < 5; index++)
        {
            var date = week.AddDays(index);
            var start = Workflows.LocalClock.Resolve(date.ToDateTime(day.DayStart), zone);
            var end = Workflows.LocalClock.Resolve(date.ToDateTime(day.DayEnd), zone);
            free += (int)DayPlanScheduler.FreeSlots(new(now > start ? now : start, end), busy).Sum(slot => slot.Length.TotalMinutes);
        }
        var remaining = value.AvailableMinutes is { } limit ? Math.Min(Math.Max(0, limit - consumed), free) : free;
        return new(week, consumed + remaining, remaining, consumed,
            $"Calendar checked {now:O}: {free} free minutes remain in Monday–Friday {day.DayStart:HH:mm}–{day.DayEnd:HH:mm} ({zone.Id}), with a five-minute buffer after events. " +
            $"{consumed} completed minutes already consume this week. " +
            (value.AvailableMinutes is { } cap ? $"Owner budget {cap} − completed {consumed}; shared remaining allocation = min({Math.Max(0, cap - consumed)}, {free}) = {remaining} minutes." :
                $"Shared remaining allocation: {remaining} minutes.") + " All-day events count as busy. No calendar event was changed.", now);
    }
}
