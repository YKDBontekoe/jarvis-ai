using Jarvis.Application.Devices;
using Jarvis.Application.Home;
using Jarvis.Application.Approvals;
using Jarvis.Application.Integrations;
using Jarvis.Application.Learning;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Infrastructure.Home;

public sealed class HomeBriefingService(
    IReminderService reminders,
    IToolApprovalStore approvals,
    IJarvisTaskService tasks,
    IOwnerSettingsStore settings,
    IDailyBriefingService briefing,
    ICalendarFeed calendar,
    IDeviceTelemetryStore telemetry,
    IIntegrationCredentialStore credentials,
    IUserMcpServerRegistry mcpServers) : IHomeBriefingService
{
    public async Task<HomeBriefingDto> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now.AddHours(36);
        var reminderList = (await reminders.ListAsync(ownerId, cancellationToken))
            .Where(item => item.Status is "pending" or "scheduled" or "active" || item.DueAt >= now)
            .Where(item => item.DueAt <= horizon && item.Status != "cancelled" && item.Place is null)
            .OrderBy(item => item.DueAt)
            .Take(8)
            .Select(item => new HomeReminderDto(item.Id, item.Title, item.DueAt, item.Status, item.Recurrence,
                item.ConversationId))
            .ToArray();
        var approvalList = (await approvals.ListActionableAsync(ownerId, cancellationToken))
            .Take(8)
            .Select(item => new HomeApprovalDto(item.Id, item.ToolName, item.CreatedAt))
            .ToArray();
        var taskList = (await tasks.ListAsync(ownerId, cancellationToken))
            .Where(item => item.Status is "queued" or "running" or "waiting" or "needs_approval")
            .OrderBy(item => item.Status == "needs_approval" ? 0 : 1)
            .Take(8)
            .Select(item => new HomeTaskDto(item.Id, item.Title, item.Status, item.CreatedAt))
            .ToArray();
        var dreaming = await settings.GetAsync<DreamingState>(ownerId, LearningSections.DreamingState, cancellationToken)
                       ?? new DreamingState();
        var brief = await briefing.GetAsync(ownerId, cancellationToken);
        IReadOnlyList<CalendarEventRecord> events = [];
        try { events = await calendar.ListUpcomingAsync(ownerId, now.AddHours(-1), horizon, cancellationToken); }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            events = [];
        }
        var calendarSecrets = await credentials.GetStatusAsync(ownerId,
            IntegrationPackIds.Provider(IntegrationPackIds.Calendar), cancellationToken);
        var device = await telemetry.GetAsync(ownerId, cancellationToken);
        var servers = await mcpServers.ListAsync(ownerId, cancellationToken);
        var packs = new List<HomePackDto>();
        foreach (var pack in IntegrationPackCatalog.All)
        {
            var status = await credentials.GetStatusAsync(ownerId, IntegrationPackIds.Provider(pack.Id), cancellationToken);
            var mcp = servers.FirstOrDefault(server =>
                server.Name.Contains(pack.Name, StringComparison.OrdinalIgnoreCase) ||
                server.AllowedTools.Any(tool => pack.SuggestedTools.Contains(tool, StringComparer.OrdinalIgnoreCase)));
            packs.Add(new HomePackDto(pack.Id, pack.Name, pack.Category,
                status is not null || mcp is not null));
        }

        return new HomeBriefingDto(
            dreaming.UserSummary,
            dreaming.UserSummaryUpdatedAt,
            reminderList,
            approvalList,
            taskList,
            new HomeCalendarDto(calendarSecrets is not null || events.Count > 0, calendarSecrets is null ? null : "ics",
                events.Select(item => new HomeCalendarEventDto(item.Title, item.StartAt, item.EndAt)).ToArray()),
            device is null ? null : new HomeDeviceDto(device.BatteryPercent, device.Charging,
                device.Latitude is not null && device.Longitude is not null, device.ReportedAt),
            brief is null ? null : new HomeBriefingScheduleDto(brief.Enabled, brief.LocalTime, brief.TimeZoneId),
            packs);
    }
}
