using Jarvis.Application.Finance;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Finance;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class SubscriptionNegotiationPersistenceTests : IAsyncLifetime
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
    public async Task The_cancel_page_and_the_drafting_task_are_stored_per_owner()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var taskId = await AddTaskAsync(owner);
        await using var database = CreateDbContext();
        var repository = new FinanceRepository(database);
        var sub = Subscription(owner);
        await repository.AddSubscriptionAsync(sub, default);
        var started = DateTimeOffset.UtcNow;

        Assert.True(await repository.UpdateSubscriptionAsync(sub with
        {
            CancelUrl = "https://www.netflix.com/cancelplan", NegotiationTaskId = taskId,
            NegotiationGoal = NegotiationGoals.LowerPrice, NegotiationStartedAt = started
        }, default));

        await using var reader = CreateDbContext();
        var stored = (await new FinanceRepository(reader).GetSubscriptionAsync(sub.Id, owner, default))!;
        Assert.Equal("https://www.netflix.com/cancelplan", stored.CancelUrl);
        Assert.Equal(taskId, stored.NegotiationTaskId);
        Assert.Equal(NegotiationGoals.LowerPrice, stored.NegotiationGoal);
        Assert.Equal(started.ToUnixTimeMilliseconds(), stored.NegotiationStartedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Null(await new FinanceRepository(reader).GetSubscriptionAsync(sub.Id, other, default));
    }

    [Fact]
    public async Task A_new_subscription_starts_with_nothing_negotiated()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new FinanceRepository(database);
        var sub = Subscription(owner);

        await repository.AddSubscriptionAsync(sub, default);

        var stored = (await repository.GetSubscriptionAsync(sub.Id, owner, default))!;
        Assert.Null(stored.CancelUrl);
        Assert.Null(stored.NegotiationTaskId);
        Assert.Null(stored.NegotiationGoal);
        Assert.Null(stored.NegotiationStartedAt);
    }

    [Fact]
    public async Task The_database_rejects_an_unknown_negotiation_goal()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new FinanceRepository(database);
        var sub = Subscription(owner);
        await repository.AddSubscriptionAsync(sub, default);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.UpdateSubscriptionAsync(sub with { NegotiationGoal = "haggle" }, default));
    }

    [Fact]
    public async Task Deleting_the_drafting_task_forgets_the_link_but_keeps_the_subscription()
    {
        var owner = Guid.CreateVersion7();
        var taskId = await AddTaskAsync(owner);
        await using var database = CreateDbContext();
        var repository = new FinanceRepository(database);
        var sub = Subscription(owner);
        await repository.AddSubscriptionAsync(sub, default);
        await repository.UpdateSubscriptionAsync(sub with
        {
            CancelUrl = "https://example.com/cancel", NegotiationTaskId = taskId,
            NegotiationGoal = NegotiationGoals.Cancel, NegotiationStartedAt = DateTimeOffset.UtcNow
        }, default);

        await database.Tasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();

        await using var reader = CreateDbContext();
        var stored = (await new FinanceRepository(reader).GetSubscriptionAsync(sub.Id, owner, default))!;
        Assert.Null(stored.NegotiationTaskId);
        Assert.Equal("https://example.com/cancel", stored.CancelUrl);
        Assert.Equal(NegotiationGoals.Cancel, stored.NegotiationGoal);
    }

    [Fact]
    public async Task A_task_cannot_be_linked_that_does_not_exist()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new FinanceRepository(database);
        var sub = Subscription(owner);
        await repository.AddSubscriptionAsync(sub, default);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.UpdateSubscriptionAsync(
            sub with { NegotiationTaskId = Guid.CreateVersion7(), NegotiationGoal = NegotiationGoals.Cancel }, default));
    }

    private static Subscription Subscription(Guid owner)
    {
        var now = DateTimeOffset.UtcNow;
        return new Subscription(Guid.CreateVersion7(), owner, "netflix", "Netflix", 15.99m, "EUR",
            SubscriptionCadences.Monthly, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 28), 6, 13.99m,
            SubscriptionStatuses.Active, null, null, now, now);
    }

    private async Task<Guid> AddTaskAsync(Guid owner)
    {
        await using var seed = CreateDbContext();
        var conversation = new Conversation(owner, "Task conversation");
        seed.Conversations.Add(conversation);
        var task = new JarvisTask(owner, "Cancel Netflix", "Write the message");
        task.AttachConversation(conversation.Id);
        seed.Tasks.Add(task);
        await seed.SaveChangesAsync();
        return task.Id;
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
