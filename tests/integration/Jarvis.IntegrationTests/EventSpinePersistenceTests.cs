using Jarvis.Application.Events;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Events;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class EventSpinePersistenceTests : IAsyncLifetime
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
    public async Task Events_are_stored_owner_scoped_filtered_and_pruned()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var watch = new EntityRef(EntityTypes.Watch, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        await using var database = CreateDbContext();
        var repository = new OwnerEventRepository(database);

        await repository.AddAsync(new JarvisEvent(owner, JarvisEventKinds.WatchFired, "Battery low", watch,
            new Dictionary<string, string> { ["value"] = "12" }, Origin: EventOrigin.System, At: now).Normalize(now), default);
        await repository.AddAsync(new JarvisEvent(owner, JarvisEventKinds.AgentActed, "Jarvis ran X",
            Origin: EventOrigin.AgentReaction, At: now.AddMinutes(1)).Normalize(now), default);
        await repository.AddAsync(new JarvisEvent(owner, JarvisEventKinds.ReminderDue, "Old",
            At: now.AddDays(-100)).Normalize(now), default);
        await repository.AddAsync(new JarvisEvent(other, JarvisEventKinds.WatchFired, "Theirs",
            At: now).Normalize(now), default);

        var all = await repository.ListAsync(owner, new OwnerEventQuery(), default);
        Assert.Equal(["Jarvis ran X", "Battery low", "Old"], all.Select(x => x.Summary));
        Assert.Equal("12", all[1].Data!["value"]);
        Assert.Equal(watch.ToString(), all[1].SubjectRef);

        Assert.Equal(["Jarvis ran X"], (await repository.ListAsync(owner,
            new OwnerEventQuery(Origins: [EventOrigin.Agent, EventOrigin.AgentReaction]), default)).Select(x => x.Summary));
        Assert.Equal(["Battery low"], (await repository.ListAsync(owner,
            new OwnerEventQuery(SubjectRef: watch.ToString()), default)).Select(x => x.Summary));
        Assert.Equal(2, (await repository.ListAsync(owner, new OwnerEventQuery(Since: now.AddDays(-1)), default)).Count);
        Assert.Single(await repository.ListAsync(other, new OwnerEventQuery(), default));

        Assert.Equal(1, await repository.PruneAsync(now.AddDays(-90), default));
        Assert.Equal(2, (await repository.ListAsync(owner, new OwnerEventQuery(), default)).Count);
    }

    [Fact]
    public async Task Related_things_come_from_links_and_existing_columns_and_stay_owner_scoped()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();

        var conversation = new Conversation(owner, "Trip planning");
        var message = new Message(conversation.Id, "user", "I prefer aisle seats");
        var reminder = new Reminder(owner, "Book the hotel", DateTimeOffset.UtcNow.AddDays(1));
        reminder.AttachConversation(conversation.Id);
        var task = new JarvisTask(owner, "Research flights", "Find flights");
        var taskConversation = new Conversation(owner, "Research flights");
        task.AttachConversation(taskConversation.Id);
        database.Conversations.Add(conversation);
        database.Conversations.Add(taskConversation);
        database.Messages.Add(message);
        database.Reminders.Add(reminder);
        database.Tasks.Add(task);
        await database.SaveChangesAsync();
        var memory = await new MemoryRepository(database).CreateAsync(owner, "preference", "Prefers aisle seats",
            0.6f, 0.9f, "conversation", message.Id, null, false, default);

        var links = new EntityLinkRepository(database);
        var related = new RelatedEntityService(database, links);
        var linker = new EntityLinker(links, related);
        var taskRef = new EntityRef(EntityTypes.Task, task.Id);
        var reminderRef = new EntityRef(EntityTypes.Reminder, reminder.Id);

        Assert.Equal(LinkOutcome.Linked,
            await linker.LinkAsync(owner, taskRef.ToString(), reminderRef.ToString(), "follows up", default));
        Assert.Equal(LinkOutcome.AlreadyLinked,
            await linker.LinkAsync(owner, taskRef.ToString(), reminderRef.ToString(), "follows_up", default));
        Assert.Equal(LinkOutcome.NotFound,
            await linker.LinkAsync(other, taskRef.ToString(), reminderRef.ToString(), null, default));

        var forReminder = await related.GetRelatedAsync(owner, reminderRef, default);
        Assert.Contains(forReminder, x => x.Ref == taskRef.ToString() && x.Relation == "follows_up" && x.Direction == "in");
        Assert.Contains(forReminder, x => x.Ref == $"conversation:{conversation.Id:D}" && x.Title == "Trip planning");

        var forMemory = await related.GetRelatedAsync(owner, new EntityRef(EntityTypes.Memory, memory.Id), default);
        Assert.Contains(forMemory, x => x.Ref == $"conversation:{conversation.Id:D}");

        var forConversation = await related.GetRelatedAsync(owner,
            new EntityRef(EntityTypes.Conversation, conversation.Id), default);
        Assert.Contains(forConversation, x => x.Ref == reminderRef.ToString());
        Assert.Contains(forConversation, x => x.Ref == $"memory:{memory.Id:D}");

        // Someone else's ref resolves to nothing at all.
        Assert.Null(await related.DescribeAsync(other, reminderRef, default));
        Assert.Empty(await related.GetRelatedAsync(other, reminderRef, default));

        Assert.True(await linker.UnlinkAsync(owner, taskRef.ToString(), reminderRef.ToString(), "follows_up", default));
        Assert.DoesNotContain(await related.GetRelatedAsync(owner, reminderRef, default), x => x.Ref == taskRef.ToString());
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
