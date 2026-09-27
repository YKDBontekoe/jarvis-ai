using Jarvis.Api.Realtime;
using Jarvis.Domain.Approvals;
using Jarvis.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Api.Notifications;

public sealed class NotificationRealtimeWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<JarvisEventsHub> hub,
    ILogger<NotificationRealtimeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private DateTimeOffset _watermark = DateTimeOffset.MinValue;
    private Guid _cursorId = Guid.Empty;
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
        var db = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
        if (!_seeded)
        {
            var latest = await db.Notifications.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest != default)
            {
                _watermark = latest;
                _cursorId = await db.Notifications.AsNoTracking()
                    .Where(x => x.CreatedAt == latest)
                    .OrderByDescending(x => x.Id)
                    .Select(x => x.Id)
                    .FirstAsync(cancellationToken);
            }
            _seeded = true;
            return;
        }

        var watermark = _watermark;
        var cursorId = _cursorId;
        var rows = await db.Notifications.AsNoTracking()
            .Where(x => x.CreatedAt > watermark || (x.CreatedAt == watermark && x.Id.CompareTo(cursorId) > 0))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

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

            if (notification.Type == "approval.required" && notification.SourceId is { } approvalId)
            {
                var approval = await db.ToolApprovals.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == approvalId && x.OwnerId == notification.OwnerId,
                        cancellationToken);
                if (approval is { Status: "pending" })
                    await PublishSafelyAsync(hub.Clients.Group(JarvisEventsHub.GroupName(approval.ConversationId)),
                        "tool.approval_required", ToApprovalEvent(approval), cancellationToken);
            }

            _watermark = notification.CreatedAt;
            _cursorId = notification.Id;
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
