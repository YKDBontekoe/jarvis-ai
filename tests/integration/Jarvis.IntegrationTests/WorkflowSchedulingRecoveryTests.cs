using Jarvis.Infrastructure.Persistence;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
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

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
