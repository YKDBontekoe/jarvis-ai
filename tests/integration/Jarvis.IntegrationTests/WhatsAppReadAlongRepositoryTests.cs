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

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
