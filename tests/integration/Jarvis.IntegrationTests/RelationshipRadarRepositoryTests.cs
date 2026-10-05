using Jarvis.Application.Channels;
using Jarvis.Application.People.Radar;
using Jarvis.Application.WhatsApp;
using Jarvis.Domain.People;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class RelationshipRadarRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
    private const string Phone = "+31611111111";
    private const string Lid = "100000000000001@lid";
    private const string OtherLid = "100000000000002@lid";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_chat_belongs_to_one_person_and_links_are_owner_scoped()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000001");
        var anna = await AddPersonAsync(owner, "Anna");
        var piet = await AddPersonAsync(owner, "Piet");
        await using var database = CreateDbContext();
        var links = new PersonLinkRepository(database);

        await links.AddAsync(Link(owner, anna, connection, Phone), default);
        await using (var second = CreateDbContext())
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new PersonLinkRepository(second).AddAsync(Link(owner, piet, connection, Phone), default));

        var stored = Assert.Single(await links.ListAsync(owner, default));
        Assert.Equal(anna, stored.PersonId);
        Assert.Empty(await links.ListAsync(other, default));
        Assert.NotNull(await links.FindAsync(owner, connection, Phone, default));
        Assert.Null(await links.FindAsync(other, connection, Phone, default));

        // Only the right owner and person can remove it.
        Assert.False(await links.DeleteAsync(stored.Id, piet, owner, default));
        Assert.False(await links.DeleteAsync(stored.Id, anna, other, default));
        Assert.True(await links.DeleteAsync(stored.Id, anna, owner, default));
        Assert.Empty(await links.ListAsync(owner, default));
    }

    [Fact]
    public async Task Tone_is_stored_on_the_link_for_its_owner_only()
    {
        var owner = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000002");
        var anna = await AddPersonAsync(owner, "Anna");
        await using var database = CreateDbContext();
        var links = new PersonLinkRepository(database);
        var link = Link(owner, anna, connection, Phone);
        await links.AddAsync(link, default);
        var at = DateTimeOffset.UtcNow;

        await links.UpdateToneAsync(link.Id, Guid.CreateVersion7(), -1, "someone else", at, default);
        Assert.Null((await links.ListAsync(owner, default)).Single().ToneAt);

        await links.UpdateToneAsync(link.Id, owner, -1, "A little cool.", at, default);
        var stored = (await links.ListAsync(owner, default)).Single();
        Assert.Equal(-1, stored.ToneScore);
        Assert.Equal("A little cool.", stored.ToneReason);
        Assert.Equal(at.ToUnixTimeMilliseconds(), stored.ToneAt!.Value.ToUnixTimeMilliseconds());

        await using var invalid = CreateDbContext();
        await Assert.ThrowsAnyAsync<Exception>(() => new PersonLinkRepository(invalid).UpdateToneAsync(
            link.Id, owner, 5, "out of range", at, default));
    }

    [Fact]
    public async Task Removing_a_person_or_a_connection_removes_the_links_but_not_the_messages()
    {
        var owner = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000003");
        var anna = await AddPersonAsync(owner, "Anna");
        var piet = await AddPersonAsync(owner, "Piet");
        await using var database = CreateDbContext();
        var links = new PersonLinkRepository(database);
        var chats = new WhatsAppAssistantRepository(database);
        await chats.SaveChatAsync(owner, connection, Phone, "Anna", true, false, default);
        await chats.StoreObservedAsync(connection, [new("m1", Phone, false, "Anna", "hoi", DateTimeOffset.UtcNow)], default);
        await links.AddAsync(Link(owner, anna, connection, Phone), default);
        await links.AddAsync(Link(owner, piet, connection, "+31622222222"), default);

        Assert.True(await new PeopleRepository(database).DeleteAsync(anna, owner, default));

        var remaining = Assert.Single(await links.ListAsync(owner, default));
        Assert.Equal(piet, remaining.PersonId);
        Assert.Single(await chats.ListMessagesAsync(owner, connection, Phone, 10, null, default));

        await database.ChannelConnections.Where(c => c.Id == connection).ExecuteDeleteAsync();
        Assert.Empty(await links.ListAsync(owner, default));
    }

    [Fact]
    public async Task Message_stats_cover_only_the_requested_chats_and_the_window_and_carry_no_text()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000004");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);
        const string second = "+31622222222";
        await chats.SaveChatAsync(owner, connection, Phone, "Anna", true, false, default);
        await chats.SaveChatAsync(owner, connection, second, "Piet", true, false, default);
        var now = DateTimeOffset.UtcNow;
        await chats.StoreObservedAsync(connection,
        [
            new("a1", Phone, false, "Anna", "secret text", now.AddDays(-2)),
            new("a2", Phone, true, null, "my reply", now.AddDays(-2).AddMinutes(5)),
            new("a3", Phone, false, "Anna", "too old", now.AddDays(-200)),
            new("p1", second, false, "Piet", "other chat", now.AddDays(-1))
        ], default);

        var stats = await chats.ListAsync(owner, [new ChatKey(connection, Phone)], now.AddDays(-120), default);

        Assert.Equal(2, stats.Count);
        Assert.All(stats, s => Assert.Equal(Phone, s.ChatId));
        Assert.Equal([true, false], stats.Select(s => s.FromMe).ToArray()); // newest first
        Assert.Empty(await chats.ListAsync(other, [new ChatKey(connection, Phone)], now.AddDays(-120), default));
        Assert.Empty(await chats.ListAsync(owner, [], now.AddDays(-120), default));
        Assert.Equal(3, (await chats.ListAsync(owner,
            [new ChatKey(connection, Phone), new ChatKey(connection, second)], now.AddDays(-120), default)).Count);
        Assert.Empty(await chats.ListAsync(owner, [new ChatKey(Guid.CreateVersion7(), Phone)], now.AddDays(-120),
            default));
    }

    [Fact]
    public async Task A_link_to_an_lid_chat_follows_the_conversation_when_it_merges_onto_the_phone_number()
    {
        var owner = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000005");
        var anna = await AddPersonAsync(owner, "Anna");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);
        var links = new PersonLinkRepository(database);
        await chats.SaveChatAsync(owner, connection, Lid, "Anna", true, false, default);
        await chats.StoreObservedAsync(connection, [new("l1", Lid, false, "Anna", "hoi", DateTimeOffset.UtcNow)], default);
        await links.AddAsync(Link(owner, anna, connection, Lid), default);

        Assert.True(await chats.MergeChatAliasesAsync(owner, connection, Phone, [Lid], default));

        var link = Assert.Single(await links.ListAsync(owner, default));
        Assert.Equal(Phone, link.ChatId);
        Assert.Equal(anna, link.PersonId);
        Assert.Single(await chats.ListMessagesAsync(owner, connection, Phone, 10, null, default));
    }

    [Fact]
    public async Task When_the_phone_number_is_already_linked_the_lid_link_is_dropped_on_merge()
    {
        var owner = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000006");
        var anna = await AddPersonAsync(owner, "Anna");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);
        var links = new PersonLinkRepository(database);
        await chats.SaveChatAsync(owner, connection, Lid, "Anna", true, false, default);
        await chats.SaveChatAsync(owner, connection, Phone, "Anna", true, false, default);
        await links.AddAsync(Link(owner, anna, connection, Phone), default);
        await links.AddAsync(Link(owner, anna, connection, Lid), default);

        Assert.True(await chats.MergeChatAliasesAsync(owner, connection, Phone, [Lid], default));

        var link = Assert.Single(await links.ListAsync(owner, default));
        Assert.Equal(Phone, link.ChatId);
    }

    [Fact]
    public async Task When_two_lid_chats_with_different_people_merge_the_oldest_link_wins()
    {
        var owner = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000007");
        var anna = await AddPersonAsync(owner, "Anna");
        var piet = await AddPersonAsync(owner, "Piet");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);
        var links = new PersonLinkRepository(database);
        await chats.SaveChatAsync(owner, connection, Lid, "Anna", true, false, default);
        await chats.SaveChatAsync(owner, connection, OtherLid, "Anna", true, false, default);
        var older = Link(owner, anna, connection, Lid) with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) };
        await links.AddAsync(older, default);
        await links.AddAsync(Link(owner, piet, connection, OtherLid), default);

        Assert.True(await chats.MergeChatAliasesAsync(owner, connection, Phone, [Lid, OtherLid], default));

        var link = Assert.Single(await links.ListAsync(owner, default));
        Assert.Equal(anna, link.PersonId);
        Assert.Equal(Phone, link.ChatId);
    }

    [Fact]
    public async Task Merging_chats_leaves_another_owners_links_alone()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var connection = await ConnectAsync(owner, "+31600000008");
        var otherConnection = await ConnectAsync(other, "+31600000009");
        var anna = await AddPersonAsync(owner, "Anna");
        var theirAnna = await AddPersonAsync(other, "Anna");
        await using var database = CreateDbContext();
        var chats = new WhatsAppAssistantRepository(database);
        var links = new PersonLinkRepository(database);
        await chats.SaveChatAsync(owner, connection, Lid, "Anna", true, false, default);
        await chats.SaveChatAsync(other, otherConnection, Lid, "Anna", true, false, default);
        await links.AddAsync(Link(owner, anna, connection, Lid), default);
        await links.AddAsync(Link(other, theirAnna, otherConnection, Lid), default);

        Assert.True(await chats.MergeChatAliasesAsync(owner, connection, Phone, [Lid], default));

        Assert.Equal(Phone, Assert.Single(await links.ListAsync(owner, default)).ChatId);
        Assert.Equal(Lid, Assert.Single(await links.ListAsync(other, default)).ChatId);
    }

    [Fact]
    public async Task Owners_with_only_a_linked_chat_still_get_the_daily_check_in()
    {
        var linkedOnly = Guid.CreateVersion7();
        var plain = Guid.CreateVersion7();
        var withCadence = Guid.CreateVersion7();
        var connection = await ConnectAsync(linkedOnly, "+31600000010");
        var anna = await AddPersonAsync(linkedOnly, "Anna");
        await AddPersonAsync(plain, "Plain");
        await AddPersonAsync(withCadence, "Cadence", contactEveryDays: 14);
        await using var database = CreateDbContext();
        await new PersonLinkRepository(database).AddAsync(Link(linkedOnly, anna, connection, Phone), default);

        var owners = await new PeopleRepository(database).ListOwnersWithCheckInsAsync(default);

        Assert.Contains(linkedOnly, owners);
        Assert.Contains(withCadence, owners);
        Assert.DoesNotContain(plain, owners);
        Assert.Equal(owners.Count, owners.Distinct().Count());
    }

    private static PersonChannelLink Link(Guid owner, Guid person, Guid connection, string chatId) =>
        new(Guid.CreateVersion7(), owner, person, connection, chatId, null, null, null, DateTimeOffset.UtcNow);

    private async Task<Guid> AddPersonAsync(Guid owner, string name, int? contactEveryDays = null)
    {
        await using var database = CreateDbContext();
        var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await new PeopleRepository(database).AddAsync(new Person(id, owner, name, null, null, null, null, null,
            contactEveryDays, null, null, null, null, now, now), default);
        return id;
    }

    private async Task<Guid> ConnectAsync(Guid owner, string phone)
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
