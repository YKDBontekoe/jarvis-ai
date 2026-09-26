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
    private readonly HashSet<Guid> _published = [];
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
            var existing = await db.Notifications.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(1_000)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in existing)
                _published.Add(id);
            _seeded = true;
            return;
        }

        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var rows = await db.Notifications.AsNoTracking()
            .Where(x => x.CreatedAt >= since)
            .OrderBy(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var notification in rows)
        {
            if (_published.Contains(notification.Id)) continue;
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

            _published.Add(notification.Id);
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
