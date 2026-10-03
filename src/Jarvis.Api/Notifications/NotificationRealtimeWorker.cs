using Jarvis.Api.Realtime;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Approvals;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Notifications;

public sealed class NotificationRealtimeWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<JarvisEventsHub> hub,
    ILogger<NotificationRealtimeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 200;
    private NotificationCursor? _cursor;
    private bool _seeded;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await PublishNewAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Realtime notification fan-out failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PublishNewAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var feed = scope.ServiceProvider.GetRequiredService<INotificationFeed>();
        if (!_seeded)
        {
            // Start after whatever already exists so a restart does not replay old notifications.
            _cursor = await feed.GetLatestCursorAsync(cancellationToken);
            _seeded = true;
            return;
        }

        var rows = await feed.ListAfterAsync(_cursor, BatchSize, cancellationToken);
        foreach (var notification in rows)
        {
            await PublishSafelyAsync(hub.Clients.Group(JarvisEventsHub.OwnerGroupName(notification.OwnerId)),
                "notification.created", new
                {
                    type = notification.Type,
                    notificationId = notification.Id,
                    title = notification.Title,
                    body = notification.Body,
                    sourceId = notification.SourceId
                }, cancellationToken);

            if (notification.Type == "approval.required" && notification.SourceId is { } approvalId &&
                await feed.FindPendingApprovalAsync(notification.OwnerId, approvalId, cancellationToken)
                    is { } approval)
                await PublishSafelyAsync(hub.Clients.Group(JarvisEventsHub.GroupName(approval.ConversationId)),
                    "tool.approval_required", ToApprovalEvent(approval), cancellationToken);

            _cursor = new NotificationCursor(notification.CreatedAt, notification.Id);
        }
    }

    private async Task PublishSafelyAsync(IClientProxy clients, string eventName, object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await clients.SendAsync(eventName, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish {EventName} for a notification fan-out.", eventName);
        }
    }

    private static object ToApprovalEvent(ToolApproval approval) => new
    {
        approval.Id,
        approval.ConversationId,
        approval.ToolName,
        approval.ArgumentsJson,
        approval.CreatedAt
    };
}
