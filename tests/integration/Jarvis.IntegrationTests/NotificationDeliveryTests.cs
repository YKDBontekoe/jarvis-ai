using Jarvis.Application.Workflows;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class NotificationDeliveryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Claims_a_due_delivery_once_and_marks_it_delivered()
    {
        var owner = Guid.CreateVersion7();
        var key = await SeedDeliveryAsync(owner, "reminder.due", sourceId: null);

        await using var database = CreateDbContext();
        var queue = new PushDeliveryRepository(database);
        Assert.Equal([key], await queue.ListDueAsync(10, CancellationToken.None));

        var claim = await queue.TryClaimAsync(key, TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.True(claim.Claimed);
        Assert.NotNull(claim.Work);
        Assert.Equal("device-token", claim.Work.DeviceToken);
        Assert.Equal(1, claim.Work.Attempts);
        Assert.Equal(key.NotificationId.ToString("D"), claim.Work.Data["notificationId"]);

        var second = await queue.TryClaimAsync(key, TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.False(second.Claimed);
        Assert.Empty(await queue.ListDueAsync(10, CancellationToken.None));

        await queue.MarkDeliveredAsync(key, CancellationToken.None);
        var delivered = await database.PushDeliveries.AsNoTracking().SingleAsync();
        Assert.NotNull(delivered.DeliveredAt);
        Assert.Null(delivered.LeaseUntil);
    }

    [Fact]
    public async Task Drops_an_approval_push_once_the_approval_is_resolved()
    {
        var owner = Guid.CreateVersion7();
        var conversation = new Conversation(owner, "Approval conversation");
        var approval = new ToolApproval(owner, conversation.Id, "request", "call", "tool", "{}");
        approval.Decide(approved: false);
        await using (var seed = CreateDbContext())
        {
            seed.Conversations.Add(conversation);
            seed.ToolApprovals.Add(approval);
            await seed.SaveChangesAsync();
        }
        var key = await SeedDeliveryAsync(owner, "approval.required", approval.Id);

        await using var database = CreateDbContext();
        var claim = await new PushDeliveryRepository(database)
            .TryClaimAsync(key, TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.True(claim.Claimed);
        Assert.Null(claim.Work);
        Assert.False(await database.PushDeliveries.AnyAsync());
    }

    [Fact]
    public async Task Failure_backs_off_and_removing_a_device_clears_its_queue()
    {
        var owner = Guid.CreateVersion7();
        var key = await SeedDeliveryAsync(owner, "reminder.due", sourceId: null);

        await using var database = CreateDbContext();
        var queue = new PushDeliveryRepository(database);
        var claim = await queue.TryClaimAsync(key, TimeSpan.FromMinutes(2), CancellationToken.None);
        await queue.RecordFailureAsync(key, claim.Work!.Attempts, "FCM returned 500.", CancellationToken.None);

        var failed = await database.PushDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal("FCM returned 500.", failed.LastError);
        Assert.True(failed.NextAttemptAt > DateTimeOffset.UtcNow);
        Assert.Empty(await queue.ListDueAsync(10, CancellationToken.None));

        await queue.RemoveDeviceAsync(key.DeviceId, CancellationToken.None);
        Assert.False(await database.PushDeliveries.AnyAsync());
        Assert.False(await database.PushDevices.AnyAsync());
    }

    [Fact]
    public async Task Feed_reads_notifications_after_the_cursor_in_creation_order()
    {
        var owner = Guid.CreateVersion7();
        await using (var seed = CreateDbContext())
        {
            seed.Notifications.AddRange(
                new Notification(Guid.CreateVersion7(), owner, "reminder.due", "First", "Body", null),
                new Notification(Guid.CreateVersion7(), owner, "reminder.due", "Second", "Body", null));
            await seed.SaveChangesAsync();
        }

        await using var database = CreateDbContext();
        var feed = new NotificationFeed(database);
        var all = await feed.ListAfterAsync(null, 10, CancellationToken.None);
        Assert.Equal(2, all.Count);

        var latest = await feed.GetLatestCursorAsync(CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Empty(await feed.ListAfterAsync(latest, 10, CancellationToken.None));

        var afterFirst = new NotificationCursor(all[0].CreatedAt, all[0].Id);
        Assert.Equal([all[1].Id], (await feed.ListAfterAsync(afterFirst, 10, CancellationToken.None)).Select(x => x.Id));
    }

    private async Task<PushDeliveryKey> SeedDeliveryAsync(Guid owner, string type, Guid? sourceId)
    {
        await using var seed = CreateDbContext();
        var device = new PushDevice(owner, "device-token", "ios");
        var notification = new Notification(Guid.CreateVersion7(), owner, type, "Title", "Body", sourceId);
        seed.PushDevices.Add(device);
        seed.Notifications.Add(notification);
        seed.PushDeliveries.Add(new PushDelivery(notification.Id, device.Id));
        await seed.SaveChangesAsync();
        return new PushDeliveryKey(notification.Id, device.Id);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
