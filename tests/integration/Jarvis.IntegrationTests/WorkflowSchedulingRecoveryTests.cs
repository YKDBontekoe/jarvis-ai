using Jarvis.Infrastructure.Persistence;
using Jarvis.Application.Files;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Files;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class WorkflowSchedulingRecoveryTests : IAsyncLifetime
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
    public async Task Stale_active_watches_are_requeued_for_temporal_restart()
    {
        var owner = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await using var database = CreateDbContext();
        var watches = new ConditionWatchRepository(database);
        var stale = await watches.CreateAsync(owner, new CreateConditionWatchRequest(
            "Price", "https://example.com/metrics.json", "value", "above", 100, 15), CancellationToken.None);
        var fresh = await watches.CreateAsync(owner, new CreateConditionWatchRequest(
            "Fresh", "https://example.com/fresh.json", "value", "below", 10, 15), CancellationToken.None);
        await watches.MarkScheduleDispatchedAsync(stale.Id, CancellationToken.None);
        await watches.MarkScheduleDispatchedAsync(fresh.Id, CancellationToken.None);
        await watches.RecordCheckAsync(stale.Id, 1,
            now.AddMinutes(-(15 + ConditionWatch.ScheduleStaleGraceMinutes + 1)), CancellationToken.None);
        await watches.RecordCheckAsync(fresh.Id, 1, now, CancellationToken.None);

        Assert.Equal(1, await watches.RequeueStaleActiveAsync(now, CancellationToken.None));
        var pending = await watches.ListPendingForSchedulingAsync(CancellationToken.None);
        Assert.Equal(stale.Id, Assert.Single(pending).Id);
    }

    [Fact]
    public async Task Overdue_dispatched_reminders_are_requeued_for_temporal_restart()
    {
        await using var database = CreateDbContext();
        var reminders = new WorkflowRepository(database);
        var overdue = new Reminder(Guid.CreateVersion7(), "Overdue", DateTimeOffset.UtcNow.AddMinutes(-5));
        overdue.MarkScheduleDispatched();
        var future = new Reminder(Guid.CreateVersion7(), "Later", DateTimeOffset.UtcNow.AddHours(2));
        future.MarkScheduleDispatched();
        database.Reminders.AddRange(overdue, future);
        await database.SaveChangesAsync();

        Assert.Equal(1, await reminders.RequeueOverdueDispatchedAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        var pending = await reminders.ListPendingForSchedulingAsync(CancellationToken.None);
        Assert.Equal(overdue.Id, Assert.Single(pending).Id);
    }

    [Fact]
    public async Task Place_reminders_fire_from_positions_and_stay_out_of_temporal()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var reminders = new WorkflowRepository(database);
        var place = new ReminderPlace("Supermarket", 52.0907, 5.1214, 150);
        var milk = await reminders.CreateAsync(owner, new CreateReminderRequest("Buy milk", DateTimeOffset.UtcNow,
            Place: place), CancellationToken.None);
        var keys = await reminders.CreateAsync(owner, new CreateReminderRequest("Keys", DateTimeOffset.UtcNow,
            Place: place with { Trigger = Reminder.LocationLeave, Repeats = true }), CancellationToken.None);
        var foreign = await reminders.CreateAsync(other, new CreateReminderRequest("Not mine", DateTimeOffset.UtcNow,
            Place: place), CancellationToken.None);

        Assert.Empty(await reminders.ListPendingForSchedulingAsync(CancellationToken.None));

        var now = DateTimeOffset.UtcNow;
        Assert.Empty(await reminders.ObservePositionAsync(owner, 52.2, 5.3, 20, now, CancellationToken.None));
        var arrived = await reminders.ObservePositionAsync(owner, 52.0908, 5.1215, 20, now.AddMinutes(1),
            CancellationToken.None);
        Assert.Equal(milk.Id, Assert.Single(arrived).ReminderId);
        Assert.Empty(await reminders.ObservePositionAsync(owner, 52.0908, 5.1215, 20, now.AddMinutes(2),
            CancellationToken.None));
        var left = await reminders.ObservePositionAsync(owner, 52.2, 5.3, 20, now.AddMinutes(3),
            CancellationToken.None);
        Assert.Equal(keys.Id, Assert.Single(left).ReminderId);

        database.ChangeTracker.Clear();
        Assert.Equal("completed", (await reminders.GetAsync(milk.Id, owner, CancellationToken.None))!.Status);
        Assert.Equal("pending", (await reminders.GetAsync(keys.Id, owner, CancellationToken.None))!.Status);
        Assert.Equal("pending", (await reminders.GetAsync(foreign.Id, other, CancellationToken.None))!.Status);
        var notifications = await reminders.ListNotificationsAsync(owner, CancellationToken.None);
        Assert.Equal(2, notifications.Count(x => x.Type == "reminder.due"));
        Assert.Empty(await reminders.ListNotificationsAsync(other, CancellationToken.None));
        Assert.Equal(0, await reminders.RequeueOverdueDispatchedAsync(DateTimeOffset.UtcNow.AddDays(1),
            CancellationToken.None));
    }

    [Fact]
    public async Task Stale_queued_tasks_are_requeued_for_temporal_restart()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversation = new Conversation(owner, "Task conversation");
        var stale = new JarvisTask(owner, "Stale", "Do the work");
        stale.AttachConversation(conversation.Id);
        stale.MarkScheduleDispatched();
        var fresh = new JarvisTask(owner, "Fresh", "Do later");
        fresh.AttachConversation(conversation.Id);
        database.Conversations.Add(conversation);
        database.Tasks.AddRange(stale, fresh);
        await database.SaveChangesAsync();

        var tasks = new WorkflowRepository(database);
        Assert.Equal(1, await tasks.RequeueStaleQueuedAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None));
        var pending = await tasks.ListQueuedForSchedulingAsync(CancellationToken.None);
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, item => item.Id == stale.Id);
        Assert.Contains(pending, item => item.Id == fresh.Id);
    }

    [Fact]
    public async Task Stale_queued_files_are_requeued_for_temporal_restart()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var files = new FileRepository(database);
        var staleId = Guid.CreateVersion7();
        var freshId = Guid.CreateVersion7();
        var recentId = Guid.CreateVersion7();
        await files.CreateAsync(new StoredFile(staleId, owner, staleId.ToString(), "stale.txt", "text/plain",
            32, new string('0', 64), DateTimeOffset.UtcNow, "queued"), CancellationToken.None);
        await files.CreateAsync(new StoredFile(freshId, owner, freshId.ToString(), "fresh.txt", "text/plain",
            32, new string('0', 64), DateTimeOffset.UtcNow, "queued"), CancellationToken.None);
        await files.CreateAsync(new StoredFile(recentId, owner, recentId.ToString(), "recent.txt", "text/plain",
            32, new string('0', 64), DateTimeOffset.UtcNow, "queued"), CancellationToken.None);
        await files.MarkProcessingScheduleDispatchedAsync(staleId, CancellationToken.None);
        await files.MarkProcessingScheduleDispatchedAsync(recentId, CancellationToken.None);
        await database.Files.Where(x => x.Id == staleId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt,
                DateTimeOffset.UtcNow.AddMinutes(-(FileIndexing.QueuedDispatchStaleMinutes + 1))));
        database.ChangeTracker.Clear();

        Assert.Equal(1, await files.RequeueStaleQueuedAsync(
            DateTimeOffset.UtcNow.AddMinutes(-FileIndexing.QueuedDispatchStaleMinutes), CancellationToken.None));
        var pending = await files.ListQueuedForProcessingAsync(CancellationToken.None);
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, item => item.Id == staleId);
        Assert.Contains(pending, item => item.Id == freshId);
        Assert.DoesNotContain(pending, item => item.Id == recentId);
    }

    [Fact]
    public async Task Stale_enabled_briefings_are_requeued_for_temporal_restart()
    {
        await using var database = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var checkAt = new DateTimeOffset(now.Year, now.Month, now.Day, 21, 0, 0, TimeSpan.Zero);
        var stale = new DailyBriefingPreference(Guid.CreateVersion7(), true, new TimeOnly(8, 0), "UTC");
        stale.MarkScheduleDispatched();
        stale.MarkDelivered(DateOnly.FromDateTime(checkAt.UtcDateTime.AddDays(-2)));
        var fresh = new DailyBriefingPreference(Guid.CreateVersion7(), true, new TimeOnly(8, 0), "UTC");
        fresh.MarkScheduleDispatched();
        fresh.MarkDelivered(DateOnly.FromDateTime(checkAt.UtcDateTime));
        database.DailyBriefings.AddRange(stale, fresh);
        await database.SaveChangesAsync();
        await database.DailyBriefings.Where(x => x.OwnerId == stale.OwnerId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt, checkAt.AddDays(-2)));
        database.ChangeTracker.Clear();

        var briefings = new DailyBriefingRepository(database);
        Assert.Equal(1, await briefings.RequeueStaleEnabledAsync(checkAt, CancellationToken.None));
        var pending = await briefings.ListPendingForSchedulingAsync(CancellationToken.None);
        Assert.Equal(stale.OwnerId, Assert.Single(pending).OwnerId);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
