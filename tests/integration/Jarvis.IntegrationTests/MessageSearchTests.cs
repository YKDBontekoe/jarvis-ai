using Jarvis.Application.Conversations;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class MessageSearchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public async Task Finds_a_word_that_is_only_in_a_message_and_only_for_its_owner()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var chat = await store.CreateAsync(owner, "Weekend plans", CancellationToken.None);
        var foreign = await store.CreateAsync(other, "Weekend plans", CancellationToken.None);
        await store.AddMessageAsync(new Message(chat.Id, "user", "Can you look at the boiler quote again?"), CancellationToken.None);
        var reply = new Message(chat.Id, "assistant", "The boiler quote from **Techno** is 40% higher_than last year.");
        await store.AddMessageAsync(reply, CancellationToken.None);
        await store.AddMessageAsync(new Message(chat.Id, "tool", "boiler lookup result"), CancellationToken.None);
        await store.AddMessageAsync(new Message(foreign.Id, "user", "my private boiler note"), CancellationToken.None);

        var hits = await store.SearchMessagesAsync(owner, "boiler", 10, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, hit => Assert.Equal("Weekend plans", hit.ConversationTitle));
        Assert.DoesNotContain(hits, hit => hit.Message.Role == "tool");
        Assert.Equal(reply.Id, hits[0].Message.Id);
        Assert.Empty(await store.SearchMessagesAsync(other, "quote", 10, CancellationToken.None));
        // % and _ are plain characters, not wildcards.
        Assert.Single(await store.SearchMessagesAsync(owner, "higher_than", 10, CancellationToken.None));
        Assert.Empty(await store.SearchMessagesAsync(owner, "100%", 10, CancellationToken.None));
        Assert.Empty(await store.SearchMessagesAsync(owner, "   ", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Hides_task_conversations_because_their_transcripts_open_from_Tasks()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var taskChat = new Conversation(owner, "Compare quotes");
        var task = new JarvisTask(owner, "Compare quotes", "Compare");
        task.AttachConversation(taskChat.Id);
        database.Conversations.Add(taskChat);
        database.Tasks.Add(task);
        await database.SaveChangesAsync();
        await store.AddMessageAsync(new Message(taskChat.Id, "assistant", "The zebra quote wins"), CancellationToken.None);

        Assert.Empty(await store.SearchMessagesAsync(owner, "zebra", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Opens_a_window_around_a_message_and_pages_both_ways_without_gaps_or_repeats()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var chat = await store.CreateAsync(owner, "Long chat", CancellationToken.None);
        var messages = new List<Message>();
        // Same timestamp on purpose: ordering must fall back to the id.
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        for (var index = 0; index < 30; index++)
        {
            var message = new Message(chat.Id, index % 2 == 0 ? "user" : "assistant", $"message {index:00}");
            typeof(Message).GetProperty(nameof(Message.CreatedAt))!.SetValue(message, at.AddSeconds(index / 3));
            messages.Add(message);
            database.Messages.Add(message);
        }
        await database.SaveChangesAsync();
        var ordered = messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
        var anchor = ordered[15];

        var window = await store.GetMessageWindowAsync(chat.Id, anchor.Id, 4, 4, CancellationToken.None);

        Assert.NotNull(window);
        Assert.Equal(ordered.Skip(11).Take(9).Select(m => m.Id), window.Items.Select(m => m.Id));
        Assert.True(window.HasOlder);
        Assert.True(window.HasNewer);

        var older = await store.GetMessagePageAsync(chat.Id, window.OlderCursor, 100, CancellationToken.None);
        Assert.Equal(ordered.Take(11).Select(m => m.Id), older.Items.Select(m => m.Id));
        Assert.False(older.HasMore);

        var newer = await store.GetMessagesAfterAsync(chat.Id, window.NewerCursor!, 100, CancellationToken.None);
        Assert.Equal(ordered.Skip(20).Select(m => m.Id), newer.Items.Select(m => m.Id));
        Assert.False(newer.HasNewer);
        Assert.Null(newer.NewerCursor);

        var first = await store.GetMessageWindowAsync(chat.Id, ordered[0].Id, 4, 4, CancellationToken.None);
        Assert.False(first!.HasOlder);
        Assert.Null(first.OlderCursor);
        Assert.Null(await store.GetMessageWindowAsync(chat.Id, Guid.CreateVersion7(), 4, 4, CancellationToken.None));
        var otherChat = await store.CreateAsync(owner, "Other", CancellationToken.None);
        Assert.Null(await store.GetMessageWindowAsync(otherChat.Id, anchor.Id, 4, 4, CancellationToken.None));
    }

    [Fact]
    public async Task The_search_provider_returns_an_excerpt_and_a_route_to_the_message()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var chat = await store.CreateAsync(owner, "Home", CancellationToken.None);
        var long_ = string.Join(' ', Enumerable.Repeat("filler words go here", 60)) + " the boiler is leaking " +
                    string.Join(' ', Enumerable.Repeat("and more filler", 60));
        var message = new Message(chat.Id, "user", long_);
        await store.AddMessageAsync(message, CancellationToken.None);

        var hit = Assert.Single(await new MessageSearchProvider(store)
            .SearchAsync(owner, "boiler", 8, CancellationToken.None));

        Assert.Equal("message", hit.Route.Kind);
        Assert.Equal(chat.Id.ToString(), hit.Route.Parameters["conversationId"]);
        Assert.Equal(message.Id.ToString(), hit.Route.Parameters["messageId"]);
        Assert.Contains("boiler is leaking", hit.Summary);
        Assert.True(hit.Summary!.Length < 220);
        Assert.StartsWith("You: …", hit.Summary);
    }

    private JarvisDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);
}
