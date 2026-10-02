namespace Jarvis.Application.Planner;

/// <summary>Kinds of rows on the Today timeline.</summary>
public static class DayTimelineKinds
{
    /// <summary>An event from the connected calendar. Read-only in Jarvis.</summary>
    public const string Event = "event";

    /// <summary>A Jarvis reminder due today.</summary>
    public const string Reminder = "reminder";

    /// <summary>A to-do from the day plan that Jarvis placed in a free block.</summary>
    public const string Focus = "focus";
}

/// <summary>One thing the owner wants to get done today, stored in the day plan.</summary>
public sealed record DayPlanItem(
    Guid Id,
    string Title,
    int Minutes,
    bool Done,
    DateTimeOffset? StartAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// The owner's plan for one local day, kept in owner settings (no table of its own). <see cref="TimeZoneId"/> is the
/// zone the app last sent, so chat tools plan in the same day as the phone.
/// </summary>
public sealed record DayPlanState(
    DateOnly Date,
    IReadOnlyList<DayPlanItem> Items,
    TimeOnly DayStart,
    TimeOnly DayEnd,
    DateTimeOffset? PlannedAt,
    string? TimeZoneId = null);

public sealed record DayTimelineEntryDto(
    string Kind,
    string? Id,
    string Title,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    bool Done);

public sealed record DayPlanItemDto(Guid Id, string Title, int Minutes, bool Done, DateTimeOffset? StartAt,
    DateTimeOffset? EndAt);

public sealed record DayFreeSlotDto(DateTimeOffset StartAt, DateTimeOffset EndAt, int Minutes);

public sealed record DayTimelineDto(
    DateOnly Date,
    string TimeZoneId,
    TimeOnly DayStart,
    TimeOnly DayEnd,
    bool CalendarConnected,
    bool CalendarUnavailable,
    IReadOnlyList<DayTimelineEntryDto> Entries,
    IReadOnlyList<DayPlanItemDto> Items,
    IReadOnlyList<DayFreeSlotDto> FreeSlots,
    int FreeMinutes,
    DateTimeOffset? PlannedAt);

/// <summary>Result of "plan my day": the new timeline plus the to-dos that did not fit.</summary>
public sealed record DayPlanResultDto(DayTimelineDto Today, int Scheduled, IReadOnlyList<DayPlanItemDto> DidNotFit);

public sealed record AddDayPlanItemRequest(string Title, int? Minutes = null);

public sealed record UpdateDayPlanItemRequest(string? Title = null, int? Minutes = null, bool? Done = null);

public sealed record SaveDayHoursRequest(TimeOnly DayStart, TimeOnly DayEnd);

public interface IDayPlannerService
{
    /// <summary>Today's timeline in <paramref name="timeZoneId"/>, or the owner's saved zone when it is null.</summary>
    Task<DayTimelineDto> GetTodayAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken);

    Task<DayPlanItemDto> AddItemAsync(Guid ownerId, AddDayPlanItemRequest request, string? timeZoneId,
        CancellationToken cancellationToken);

    Task<DayPlanItemDto?> UpdateItemAsync(Guid ownerId, Guid itemId, UpdateDayPlanItemRequest request,
        string? timeZoneId, CancellationToken cancellationToken);

    Task<bool> RemoveItemAsync(Guid ownerId, Guid itemId, string? timeZoneId, CancellationToken cancellationToken);

    /// <summary>Places every open to-do in the free time left today, around calendar events.</summary>
    Task<DayPlanResultDto> PlanAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken);

    /// <summary>Takes every open to-do off the timeline again.</summary>
    Task<DayTimelineDto> ClearPlanAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken);

    Task<DayTimelineDto> SaveHoursAsync(Guid ownerId, SaveDayHoursRequest request, string? timeZoneId,
        CancellationToken cancellationToken);
}
