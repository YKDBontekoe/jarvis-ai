using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Endpoints;

internal static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/notifications", async (INotificationRepository notifications, ICurrentUser currentUser,
                CancellationToken ct) =>
                Results.Ok((await notifications.ListNotificationsAsync(currentUser.OwnerId, ct))
                    .Select(notification => notification.ToDto())))
            .WithName("ListNotifications");

        api.MapPost("/notifications/{id:guid}/read", async (Guid id, INotificationRepository notifications,
                ICurrentUser currentUser, CancellationToken ct) =>
            await notifications.MarkReadAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("MarkNotificationRead");

        // Done and Snooze buttons on a reminder notification. Approvals have no actions here: they are always
        // decided in the app.
        api.MapPost("/notifications/{id:guid}/actions", async (Guid id, NotificationActionRequest request,
            INotificationQuickActionService quickActions, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await quickActions.RunAsync(id, currentUser.OwnerId, request.Action, request.Minutes, ct);
            return result.Outcome switch
            {
                NotificationQuickActionOutcome.Done or NotificationQuickActionOutcome.Snoozed => Results.Ok(
                    new NotificationActionResponse(result.Outcome == NotificationQuickActionOutcome.Done
                        ? NotificationQuickActions.Done
                        : NotificationQuickActions.Snooze, result.Reminder?.ToDto())),
                NotificationQuickActionOutcome.NotFound => Results.NotFound(),
                NotificationQuickActionOutcome.NotSupported => EndpointHelpers.Invalid("notification",
                    "This notification has no quick actions. Open it in Jarvis instead."),
                NotificationQuickActionOutcome.InvalidSnooze => EndpointHelpers.Invalid("minutes",
                    "Snooze for 1 minute up to 24 hours."),
                NotificationQuickActionOutcome.ReminderGone => EndpointHelpers.Invalid("reminder",
                    "That reminder was cancelled or removed."),
                _ => EndpointHelpers.Invalid("action", "Use done or snooze.")
            };
        }).WithName("RunNotificationAction");

        api.MapPut("/push-devices", async (PushDeviceRequest request, IPushDeviceRepository devices,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var token = request.Token?.Trim();
            var platform = request.Platform?.Trim().ToLowerInvariant();
            if (!IsValidToken(token))
                return EndpointHelpers.Invalid("token", "A device token of 1 to 4,096 characters is required.");
            if (platform is not ("android" or "ios"))
                return EndpointHelpers.Invalid("platform", "Platform must be android or ios.");
            return Results.Ok(await devices.RegisterAsync(currentUser.OwnerId, token!, platform, ct));
        }).WithName("RegisterPushDevice");

        api.MapDelete("/push-devices", async ([Microsoft.AspNetCore.Mvc.FromBody] PushDeviceRequest request,
            IPushDeviceRepository devices, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var token = request.Token?.Trim();
            if (!IsValidToken(token))
                return EndpointHelpers.Invalid("token", "A device token of 1 to 4,096 characters is required.");
            return await devices.RemoveAsync(currentUser.OwnerId, token!, ct)
                ? Results.NoContent()
                : Results.NotFound();
        }).WithName("RemovePushDevice");

        api.MapGet("/audit", async (int? limit, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await audit.ListAsync(currentUser.OwnerId, Math.Clamp(limit ?? 100, 1, 200), ct))
                    .Select(item => item.ToDto())))
            .WithName("ListAuditEvents");

        return api;
    }

    private static bool IsValidToken(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length <= 4096;
}
