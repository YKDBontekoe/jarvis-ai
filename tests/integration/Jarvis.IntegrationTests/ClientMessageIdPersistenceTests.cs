using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class ClientMessageIdPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    private static Message Sent(Guid chat, string text, string? id) =>
        new(chat, "user", text, clientMessageId: id,
            clientPayloadHash: id is null ? null : "hash-of-" + text);

    [Fact]
    public async Task A_stored_send_is_found_by_its_client_id_within_its_conversation_only()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var first = await store.CreateAsync(owner, "One", CancellationToken.None);
        var second = await store.CreateAsync(owner, "Two", CancellationToken.None);
        var message = Sent(first.Id, "hello", "client-1");
        await store.AddMessageAsync(message, CancellationToken.None);

        var found = await store.FindByClientMessageIdAsync(first.Id, "client-1", CancellationToken.None);

        Assert.Equal(message.Id, found?.Id);
        Assert.Equal("hash-of-hello", found?.ClientPayloadHash);
        Assert.Null(await store.FindByClientMessageIdAsync(second.Id, "client-1", CancellationToken.None));
        Assert.Null(await store.FindByClientMessageIdAsync(first.Id, "client-2", CancellationToken.None));
    }

    [Fact]
    public async Task The_same_id_cannot_be_stored_twice_in_a_conversation_but_can_in_another()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var first = await store.CreateAsync(owner, "One", CancellationToken.None);
        var second = await store.CreateAsync(owner, "Two", CancellationToken.None);
        await store.AddMessageAsync(Sent(first.Id, "hello", "client-1"), CancellationToken.None);

        await store.AddMessageAsync(Sent(second.Id, "hello", "client-1"), CancellationToken.None);
        await using var other = CreateDbContext();
        var duplicate = new ConversationStore(other);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            duplicate.AddMessageAsync(Sent(first.Id, "hello again", "client-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_messages_without_an_id_are_unaffected()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var chat = await store.CreateAsync(owner, "Legacy", CancellationToken.None);

        await store.AddMessageAsync(Sent(chat.Id, "same", null), CancellationToken.None);
        await store.AddMessageAsync(Sent(chat.Id, "same", null), CancellationToken.None);

        Assert.Equal(2, (await store.GetMessagesAsync(chat.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Another_owner_cannot_reach_the_conversation_to_replay_into_it()
    {
        var owner = Guid.CreateVersion7();
        var intruder = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var chat = await store.CreateAsync(owner, "Mine", CancellationToken.None);
        await store.AddMessageAsync(Sent(chat.Id, "hello", "client-1"), CancellationToken.None);

        // The turn service resolves the conversation for the caller first; a foreign owner gets nothing.
        Assert.Null(await store.GetAsync(chat.Id, intruder, CancellationToken.None));
    }

    private JarvisDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);
}
