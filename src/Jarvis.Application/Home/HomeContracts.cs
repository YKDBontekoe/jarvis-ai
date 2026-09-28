namespace Jarvis.Application.Home;

public sealed record HomeBriefingDto(
    string? Portrait,
    DateTimeOffset? PortraitUpdatedAt,
    IReadOnlyList<HomeReminderDto> Reminders,
    IReadOnlyList<HomeApprovalDto> Approvals,
    IReadOnlyList<HomeTaskDto> Tasks,
    HomeCalendarDto Calendar,
    HomeDeviceDto? Device,
    HomeBriefingScheduleDto? Briefing,
    IReadOnlyList<HomePackDto> Packs);

public sealed record HomeReminderDto(Guid Id, string Title, DateTimeOffset DueAt, string Status, string Recurrence);
public sealed record HomeApprovalDto(Guid Id, string ToolName, DateTimeOffset CreatedAt);
public sealed record HomeTaskDto(Guid Id, string Title, string Status, DateTimeOffset CreatedAt);
public sealed record HomeCalendarEventDto(string Title, DateTimeOffset StartAt, DateTimeOffset? EndAt);
public sealed record HomeCalendarDto(bool Connected, string? Source, IReadOnlyList<HomeCalendarEventDto> Events);
public sealed record HomeDeviceDto(int? BatteryPercent, bool? Charging, bool HasLocation, DateTimeOffset ReportedAt);
public sealed record HomeBriefingScheduleDto(bool Enabled, TimeOnly LocalTime, string TimeZoneId);
public sealed record HomePackDto(string Id, string Name, string Category, bool Installed);

public interface IHomeBriefingService
{
    Task<HomeBriefingDto> GetAsync(Guid ownerId, CancellationToken cancellationToken);
}
