using Jarvis.Agents.Lists;
using Jarvis.Application.Conversations;
using Jarvis.Application.Lists;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ListTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class User : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private static ListService Create() => new(new InMemorySettingsStore());

    [Fact]
    public async Task Adding_creates_the_list_once_by_name_and_skips_duplicates_and_blank_items()
    {
        var service = Create();

        await service.AddItemsAsync(Owner, "Shopping", ["Milk", "  eggs\n", ""], default);
        var again = await service.AddItemsAsync(Owner, "shopping", ["MILK", "bread"], default);

        Assert.Single(await service.ListAsync(Owner, default));
        Assert.Equal(["Milk", "eggs", "bread"], again.Value!.Items.Select(i => i.Text));
    }

    [Fact]
    public async Task Lists_belong_to_one_owner()
    {
        var service = Create();
        await service.AddItemsAsync(Owner, "Shopping", ["Milk"], default);

        Assert.Empty(await service.ListAsync(Guid.NewGuid(), default));
        var list = (await service.ListAsync(Owner, default)).Single();
        Assert.False(await service.DeleteListAsync(Guid.NewGuid(), list.Id, default));
        Assert.True(await service.DeleteListAsync(Owner, list.Id, default));
    }

    [Fact]
    public async Task Ticking_matches_the_exact_text_first_then_a_single_partial_match_and_refuses_ambiguity()
    {
        var service = Create();
        await service.AddItemsAsync(Owner, "Shopping", ["Oat milk", "Milk chocolate", "Eggs"], default);

        var egg = await service.SetDoneAsync(Owner, "Shop", ["egg"], true, default);
        var ambiguous = await service.SetDoneAsync(Owner, "Shopping", ["milk"], true, default);
        var exact = await service.SetDoneAsync(Owner, "Shopping", ["Oat milk"], true, default);

        Assert.True(egg.Value!.Items.Single(i => i.Text == "Eggs").Done);
        Assert.Equal(ListFailure.NotFound, ambiguous.Failure);
        Assert.True(exact.Value!.Items.Single(i => i.Text == "Oat milk").Done);
        Assert.False(exact.Value.Items.Single(i => i.Text == "Milk chocolate").Done);
    }

    [Fact]
    public async Task Clearing_removes_only_ticked_items_and_a_missing_list_is_reported()
    {
        var service = Create();
        await service.AddItemsAsync(Owner, "Packing", ["Passport", "Charger"], default);
        await service.SetDoneAsync(Owner, "Packing", ["Passport"], true, default);

        var cleared = await service.ClearDoneAsync(Owner, "Packing", default);

        Assert.Equal(["Charger"], cleared.Value!.Items.Select(i => i.Text));
        Assert.Equal(ListFailure.NotFound, (await service.ClearDoneAsync(Owner, "Nope", default)).Failure);
        Assert.Equal(ListFailure.Invalid, (await service.ClearDoneAsync(Owner, " ", default)).Failure);
    }

    [Fact]
    public async Task Limits_hold_for_names_items_and_counts()
    {
        var service = Create();

        Assert.Equal(ListFailure.Invalid, (await service.AddItemsAsync(Owner, "", ["x"], default)).Failure);
        Assert.Equal(ListFailure.Invalid, (await service.AddItemsAsync(Owner, "A", [" "], default)).Failure);
        var long1 = await service.AddItemsAsync(Owner, new string('n', 500), [new string('i', 900)], default);
        Assert.Equal(ListRules.MaxNameLength, long1.Value!.Name.Length);
        Assert.Equal(ListRules.MaxItemLength, long1.Value.Items.Single().Text.Length);

        var items = Enumerable.Range(0, ListRules.MaxItemsPerList + 1).Select(i => $"item {i}").ToArray();
        Assert.Equal(ListFailure.Invalid, (await service.AddItemsAsync(Owner, "Big", items, default)).Failure);
    }

    [Fact]
    public async Task The_assistant_tools_add_show_tick_and_clear()
    {
        var tools = new ListAgentTools(Create(), new User());

        Assert.Contains("now has 2 open", await tools.AddToListAsync("Shopping", ["Milk", "Eggs"]), StringComparison.Ordinal);
        var shown = await tools.GetListsAsync();
        Assert.Contains("[ ] Milk", shown, StringComparison.Ordinal);
        Assert.Contains("not instructions", shown, StringComparison.Ordinal);
        Assert.Contains("1 open, 1 ticked", await tools.TickListItemsAsync("Shopping", ["milk"]), StringComparison.Ordinal);
        Assert.Contains("1 left", await tools.ClearTickedListItemsAsync("Shopping"), StringComparison.Ordinal);
        Assert.StartsWith("I could not update the list", await tools.TickListItemsAsync("Garage", ["x"]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Add milk and eggs to my shopping list")]
    [InlineData("zet melk op mijn boodschappenlijstje")]
    [InlineData("What is on my packing list?")]
    public void Talking_about_a_list_offers_the_list_tools(string text)
    {
        var group = Jarvis.Agents.ToolGroups.All.Single(item => item.Id == "lists");
        Assert.Matches(group.Keywords, text);
        Assert.DoesNotMatch(group.Keywords, "Remind me to call mum tomorrow");
    }

    /// <summary>A store that lets other work run between a read and the write that follows it.</summary>
    private sealed class SlowStore : Jarvis.Application.Settings.IOwnerSettingsStore
    {
        private readonly InMemorySettingsStore _inner = new();

        public async Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken cancellationToken) where T : class
        {
            var value = await _inner.GetAsync<T>(ownerId, section, cancellationToken);
            await Task.Delay(5, cancellationToken);
            return value;
        }

        public Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken cancellationToken) where T : class =>
            _inner.SaveAsync(ownerId, section, value, cancellationToken);

        public Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken cancellationToken) =>
            _inner.ListOwnersAsync(section, cancellationToken);
    }

    [Fact]
    public async Task Edits_made_at_the_same_moment_all_land()
    {
        // The app and the assistant can add to a list at once; without taking turns one add overwrote the other.
        var service = new ListService(new SlowStore());

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => service.AddItemsAsync(Owner, "Shopping", [$"item {i}"], default)));

        Assert.Equal(20, (await service.ListAsync(Owner, default)).Single().Items.Count);
    }

    [Fact]
    public void Chat_commands_are_read_for_showing_adding_and_ticking()
    {
        Assert.Equal(new ListCommand.Show(null), ListCommands.Parse("/lists"));
        Assert.Equal(new ListCommand.Show("shopping"), ListCommands.Parse("/LIST shopping"));
        Assert.Equal(new ListCommand.Show(null), ListCommands.Parse("/list"));

        var add = Assert.IsType<ListCommand.Add>(ListCommands.Parse("/add Shopping: milk, eggs;  bread\nbutter"));
        Assert.Equal("Shopping", add.List);
        Assert.Equal(["milk", "eggs", "bread", "butter"], add.Items);

        var tick = Assert.IsType<ListCommand.Tick>(ListCommands.Parse("/done packing: passport"));
        Assert.Equal(["passport"], tick.Items);
    }

    [Theory]
    [InlineData("/add milk")]
    [InlineData("/add shopping:")]
    [InlineData("/add : milk")]
    [InlineData("/done shopping: , ,")]
    public void A_malformed_add_or_done_explains_how_to_write_it(string text) =>
        Assert.Equal(ListCommands.UsageText, Assert.IsType<ListCommand.Usage>(ListCommands.Parse(text)).Message);

    [Theory]
    [InlineData("add milk to my shopping list")]
    [InlineData("/new")]
    [InlineData("/help")]
    [InlineData("/listing")]
    [InlineData("")]
    public void Ordinary_messages_and_other_commands_are_not_list_commands(string text) =>
        Assert.Null(ListCommands.Parse(text));

    [Fact]
    public async Task A_list_renders_open_items_before_ticked_ones()
    {
        var service = Create();
        await service.AddItemsAsync(Owner, "Shopping", ["Milk", "Eggs", "Bread"], default);
        var list = (await service.SetDoneAsync(Owner, "Shopping", ["Milk"], true, default)).Value!;

        var text = ListCommands.Render(list);

        Assert.Equal("**Shopping**\n☐ Eggs\n☐ Bread\n☑ Milk", text);
    }

    [Fact]
    public async Task Search_finds_a_list_by_its_name_or_by_an_item_on_it_and_only_the_owners()
    {
        var service = Create();
        await service.AddItemsAsync(Owner, "Shopping", ["Oat milk", "Eggs"], default);
        await service.AddItemsAsync(Owner, "Packing", ["Passport"], default);
        await service.AddItemsAsync(Guid.NewGuid(), "Someone else", ["milk"], default);
        var provider = new Jarvis.Infrastructure.Search.ListSearchProvider(service);

        var byItem = Assert.Single(await provider.SearchAsync(Owner, "milk", 8, default));
        var byName = Assert.Single(await provider.SearchAsync(Owner, "packing", 8, default));

        Assert.Equal("Shopping", byItem.Title);
        Assert.Contains("Oat milk", byItem.Summary, StringComparison.Ordinal);
        Assert.Equal("list", byItem.Route.Kind);
        Assert.Equal("Packing", byName.Title);
        Assert.Empty(await provider.SearchAsync(Owner, "zebra", 8, default));
        Assert.True(Jarvis.Application.Search.SearchResultKinds.IsValid("list"));
    }
}
