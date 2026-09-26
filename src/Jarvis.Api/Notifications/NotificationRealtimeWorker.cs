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
    private readonly HashSet<Guid> _publishedAtWatermark = [];
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
                var ids = await db.Notifications.AsNoTracking()
                    .Where(x => x.CreatedAt == latest)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                foreach (var id in ids)
                    _publishedAtWatermark.Add(id);
            }
            _seeded = true;
            return;
        }

        var watermark = _watermark;
        var rows = await db.Notifications.AsNoTracking()
            .Where(x => x.CreatedAt >= watermark)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var notification in rows)
        {
            if (notification.CreatedAt == _watermark && _publishedAtWatermark.Contains(notification.Id))
                continue;

            await hub.Clients.Group(JarvisEventsHub.OwnerGroupName(notification.OwnerId))
                .SendAsync("notification.created", new
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
                if (approval is not null)
                    await hub.Clients.Group(JarvisEventsHub.GroupName(approval.ConversationId))
                        .SendAsync("tool.approval_required", ToApprovalEvent(approval), cancellationToken);
            }

            if (notification.CreatedAt > _watermark)
            {
                _watermark = notification.CreatedAt;
                _publishedAtWatermark.Clear();
            }

            _publishedAtWatermark.Add(notification.Id);
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
