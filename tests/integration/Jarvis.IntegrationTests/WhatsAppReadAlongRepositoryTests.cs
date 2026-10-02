using Jarvis.Application.Channels;
using Jarvis.Application.WhatsApp;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class WhatsAppReadAlongRepositoryTests : IAsyncLifetime
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
    public async Task Stores_only_read_along_chats_once_and_keeps_owners_apart()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var connection = await LinkAsync(owner, "+31600000001");
        var otherConnection = await LinkAsync(other, "+31600000002");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);

        Assert.Null(await chats.SaveChatAsync(other, connection, "+31611111111", "Piet", true, true,
            CancellationToken.None));
        var piet = await chats.SaveChatAsync(owner, connection, "+31611111111", "Piet", true, true,
            CancellationToken.None);
        await chats.SaveChatAsync(owner, connection, "+31622222222", "Sanne", false, true, CancellationToken.None);
        Assert.Equal(["+31611111111"], await chats.ListWatchedChatIdsAsync(connection, CancellationToken.None));

        var sent = DateTimeOffset.UtcNow.AddMinutes(-1);
        ObservedWhatsAppMessage[] batch =
        [
            new("wa:1", "+31611111111", false, "Piet", "Etentje vrijdag om 19:00?", sent),
            new("wa:2", "+31611111111", true, null, "Ja leuk!", sent.AddSeconds(5)),
            new("wa:3", "+31622222222", false, "Sanne", "Not read along", sent),
            new("wa:1", "+31611111111", false, "Piet", "Duplicate", sent)
        ];
        Assert.Equal(2, await chats.StoreObservedAsync(connection, batch, CancellationToken.None));
        Assert.Equal(0, await chats.StoreObservedAsync(otherConnection, batch, CancellationToken.None));

        var messages = await chats.ListMessagesAsync(owner, connection, "+31611111111", 10, null,
            CancellationToken.None);
        Assert.Equal(["Ja leuk!", "Etentje vrijdag om 19:00?"], messages.Select(x => x.Text));
        Assert.Empty(await chats.ListMessagesAsync(other, connection, "+31611111111", 10, null,
            CancellationToken.None));
        Assert.Single(await chats.SearchAsync(owner, "vrijdag", null, null, 10, CancellationToken.None));
        Assert.Empty(await chats.SearchAsync(owner, "%", null, null, 10, CancellationToken.None));
        Assert.Empty(await chats.SearchAsync(other, "vrijdag", null, null, 10, CancellationToken.None));

        // The scan waits for a quiet moment, then sees exactly the new messages once.
        Assert.Empty(await chats.ClaimScanBatchesAsync(TimeSpan.FromMinutes(5), 5, 10, CancellationToken.None));
        var claimed = Assert.Single(await chats.ClaimScanBatchesAsync(TimeSpan.Zero, 5, 10, CancellationToken.None));
        Assert.Equal(piet!.Id, claimed.Chat.Id);
        Assert.Equal(2, claimed.NewMessages.Count);
        Assert.Empty(await chats.ClaimScanBatchesAsync(TimeSpan.Zero, 5, 10, CancellationToken.None));
        await chats.CompleteScanAsync(claimed.Chat.Id, claimed.ScannedThrough, CancellationToken.None);
        Assert.Empty(await chats.ClaimScanBatchesAsync(TimeSpan.Zero, 5, 10, CancellationToken.None));

        await chats.StoreObservedAsync(connection,
            [new ObservedWhatsAppMessage("wa:4", "+31611111111", false, "Piet", "Top", DateTimeOffset.UtcNow)],
            CancellationToken.None);
        var next = Assert.Single(await chats.ClaimScanBatchesAsync(TimeSpan.Zero, 5, 10, CancellationToken.None));
        Assert.Equal(["Top"], next.NewMessages.Select(x => x.Text));
        Assert.Equal(2, next.Context.Count);

        Assert.Equal(3, await chats.ClearHistoryAsync(owner, connection, "+31611111111", CancellationToken.None));
        await chats.SaveChatAsync(owner, connection, "+31611111111", "Piet", false, true, CancellationToken.None);
        Assert.Empty(await chats.ListWatchedChatIdsAsync(connection, CancellationToken.None));

        // Disconnecting the WhatsApp account removes its chats and messages.
        await chats.StoreObservedAsync(connection,
            [new ObservedWhatsAppMessage("wa:5", "+31611111111", false, "Piet", "x", DateTimeOffset.UtcNow)],
            CancellationToken.None);
        await new ChannelRepository(database).DeleteAsync(owner, connection, CancellationToken.None);
        Assert.Equal(0, await database.WhatsAppChats.CountAsync(x => x.ConnectionId == connection));
        Assert.Equal(0, await database.WhatsAppMessages.CountAsync(x => x.ConnectionId == connection));
    }

    private async Task<Guid> LinkAsync(Guid owner, string phone)
    {
        await using var database = CreateDbContext();
        var created = await new ChannelRepository(database).CreateAsync(owner,
            new SaveChannelRequest(ChannelKinds.WhatsAppLinked, "WhatsApp", phone, true, [phone], false, phone, null),
            CancellationToken.None, Guid.CreateVersion7());
        return created.Id;
    }

    [Fact]
    public async Task Preview_and_read_state_are_owner_scoped_and_do_not_mark_later_arrivals()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var connection = await LinkAsync(owner, "+31600000003");
        await using var database = CreateDbContext();
        var clock = new ReadStateClock();
        var chats = new WhatsAppAssistantRepository(database, clock);
        const string peer = "+31611111111";
        await chats.SaveChatAsync(owner, connection, peer, "Piet", true, false, default);
        await chats.StoreObservedAsync(connection, [
            new("first", peer, false, "Piet", "Hello", clock.Now),
            new("reply", peer, true, null, "My reply", clock.Now.AddSeconds(1))
        ], default);

        var activity = Assert.Single(await chats.ListActivityAsync(owner, connection, default));
        Assert.Equal("My reply", activity.Preview);
        Assert.True(activity.FromMe);
        Assert.Equal(1, activity.UnreadCount);
        Assert.Empty(await chats.ListActivityAsync(other, connection, default));
        var loaded = await chats.ListMessagesAsync(owner, connection, peer, 60, null, default);
        Assert.False(await chats.MarkReadAsync(other, connection, peer, loaded[0].Id, default));
        Assert.False(await chats.MarkReadAsync(owner, connection, "+31699999999", loaded[0].Id, default));

        clock.Now = clock.Now.AddSeconds(10);
        // Arriving after the client loaded its page, with an older WhatsApp timestamp.
        await chats.StoreObservedAsync(connection, [new("late", peer, false, "Piet", "Delayed message", clock.Now.AddMinutes(-5))], default);
        Assert.True(await chats.MarkReadAsync(owner, connection, peer, loaded[0].Id, default));
        Assert.Equal(1, Assert.Single(await chats.ListActivityAsync(owner, connection, default)).UnreadCount);
        var late = (await chats.ListMessagesAsync(owner, connection, peer, 60, null, default)).Single(m => m.ExternalId == "late");
        Assert.True(await chats.MarkReadAsync(owner, connection, peer, late.Id, default));
        await chats.MarkReadAsync(owner, connection, peer, loaded[0].Id, default);
        Assert.Equal(0, Assert.Single(await chats.ListActivityAsync(owner, connection, default)).UnreadCount);
        await chats.SaveChatAsync(owner, connection, peer, "Piet", false, false, default);
        Assert.Empty(await chats.ListActivityAsync(owner, connection, default));
    }

    [Fact]
    public async Task Message_cursor_keeps_every_message_when_timestamps_are_equal()
    {
        var owner = Guid.CreateVersion7();
        var connection = await LinkAsync(owner, "+31600000004");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);
        const string peer = "+31611111111";
        await chats.SaveChatAsync(owner, connection, peer, "Piet", true, false, default);
        var sent = DateTimeOffset.UtcNow;
        await chats.StoreObservedAsync(connection, Enumerable.Range(0, 75)
            .Select(i => new ObservedWhatsAppMessage($"same-second-{i}", peer, false, "Piet", $"Message {i}", sent)).ToArray(), default);
        var first = await chats.ListMessagesAsync(owner, connection, peer, 60, null, default);
        var older = await chats.ListMessagesAsync(owner, connection, peer, 60, null, default, first[^1].Id);
        Assert.Equal(60, first.Count);
        Assert.Equal(15, older.Count);
        Assert.Equal(75, first.Concat(older).Select(m => m.Id).Distinct().Count());
        Assert.Empty(await chats.ListMessagesAsync(owner, connection, peer, 60, null, default, older[^1].Id));
        Assert.Empty(await chats.ListMessagesAsync(Guid.CreateVersion7(), connection, peer, 60, null, default, first[^1].Id));
        Assert.Empty(await chats.ListMessagesAsync(owner, connection, "+31699999999", 60, null, default, first[^1].Id));
    }

    private sealed class ReadStateClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
