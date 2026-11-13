using System.Text.Json;
using Jarvis.Agents.Home;
using Jarvis.Application.Conversations;
using Jarvis.Application.Events;
using Jarvis.Application.Home;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeLayoutTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class User : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private static (HomeLayoutService Service, List<JarvisEvent> Events) Create()
    {
        var events = new List<JarvisEvent>();
        var bus = Fake<IJarvisEventBus>.Create(("PublishAsync", args =>
        {
            events.Add((JarvisEvent)args[0]!);
            return Task.CompletedTask;
        }));
        return (new HomeLayoutService(new InMemorySettingsStore(), bus), events);
    }

    private static IEnumerable<string> Ids(HomeLayoutView view) => view.Tiles.Select(t => t.Id);

    [Fact]
    public void The_catalog_lists_every_tile_the_app_offers_with_valid_sizes_and_the_default_layout_fits_it()
    {
        Assert.True(HomeTileCatalog.All.Count >= 40);
        Assert.All(HomeTileCatalog.All, spec =>
        {
            Assert.NotEmpty(spec.Name);
            Assert.NotEmpty(spec.Sizes);
            Assert.All(spec.Sizes, size => Assert.Contains(size, HomeTileSizes.All));
        });
        Assert.Equal(HomeLayoutRules.DefaultTiles, HomeLayoutRules.Normalize(HomeLayoutRules.DefaultTiles));
        Assert.Equal("finance", HomeTileCatalog.Find("expenses")!.Id);
        Assert.Equal("know-me", HomeTileCatalog.Resolve("Know me")!.Id);
    }

    [Fact]
    public async Task An_owner_who_never_saved_sees_the_default_and_the_first_app_save_is_not_undoable()
    {
        var (service, events) = Create();

        var empty = await service.GetAsync(Owner, default);
        Assert.False(empty.Stored);
        Assert.Equal(HomeLayoutRules.DefaultTiles, empty.Tiles);

        var saved = await service.SaveFromAppAsync(Owner,
            [new("chats", "large"), new("bogus", "square"), new("chats", "icon"), new("voice", "large"), new("expenses", "strip")],
            ["brief", "nonsense"], default);

        Assert.True(saved.View!.Stored);
        // Unknown and repeated tiles drop, unsupported sizes move to the nearest, merged ids follow their feature.
        Assert.Equal([new("chats", "large"), new("voice", "strip"), new("finance", "strip")], saved.View.Tiles);
        Assert.Equal(["brief"], saved.View.HiddenCards);
        Assert.False(saved.View.CanUndo);
        Assert.Empty(events);
        Assert.Equal(HomeLayoutRules.DefaultTiles, (await service.GetAsync(Guid.NewGuid(), default)).Tiles);
    }

    [Fact]
    public async Task Jarvis_pins_moves_and_resizes_by_id_or_name_and_announces_it_with_the_reason()
    {
        var (service, events) = Create();

        var added = await service.PinAsync(Owner, "Weekly review", null, 1, "You review every Sunday", default);
        Assert.Equal("weekly-review", Ids(added.View!).First());
        Assert.Equal("square", added.View!.Tiles[0].Size);
        Assert.Equal(HomeLayoutAuthors.Jarvis, added.View.ChangedBy);
        Assert.Equal("You review every Sunday", added.View.Note);
        Assert.True(added.View.CanUndo);

        var moved = await service.PinAsync(Owner, "habits", "wide", 2, null, default);
        Assert.Equal(["weekly-review", "habits"], Ids(moved.View!).Take(2));
        Assert.Equal("wide", moved.View!.Tiles[1].Size);
        Assert.Equal(1, moved.View.Tiles.Count(t => t.Id == "habits"));

        // A size the tile does not offer is fitted, not refused; a made-up size is refused.
        var fitted = await service.PinAsync(Owner, "voice", "large", null, null, default);
        Assert.Equal("strip", fitted.View!.Tiles.Single(t => t.Id == "voice").Size);
        Assert.False((await service.PinAsync(Owner, "voice", "giant", null, null, default)).Succeeded);
        Assert.False((await service.PinAsync(Owner, "not a tile", null, null, null, default)).Succeeded);

        Assert.Equal(3, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(HomeLayoutService.ChangedEventKind, e.Kind);
            Assert.Equal(EventOrigin.Agent, e.Origin);
            Assert.Equal(Owner, e.OwnerId);
        });
        Assert.Contains("You review every Sunday", events[0].Summary);
    }

    [Fact]
    public async Task Arranging_replaces_the_grid_keeps_sizes_when_none_are_given_and_refuses_unknown_tiles()
    {
        var (service, _) = Create();

        var arranged = await service.ArrangeAsync(Owner, [new("habits", ""), new("today", ""), new("journal", "square")],
            "Health first", default);
        Assert.Equal([new("habits", "strip"), new("today", "wide"), new("journal", "square")], arranged.View!.Tiles);

        var refused = await service.ArrangeAsync(Owner, [new("today", "wide"), new("teleporter", "icon")], null, default);
        Assert.False(refused.Succeeded);
        Assert.Contains("teleporter", refused.Error);
        Assert.False((await service.ArrangeAsync(Owner, [], null, default)).Succeeded);
        Assert.Equal(3, (await service.GetAsync(Owner, default)).Tiles.Count);
    }

    [Fact]
    public async Task Removing_hiding_and_undoing_work_and_undo_can_itself_be_undone()
    {
        var (service, _) = Create();
        await service.SaveFromAppAsync(Owner, [new("today", "wide"), new("finance", "strip")], [], default);

        var removed = await service.RemoveAsync(Owner, "Finance", "You never open it", default);
        Assert.Equal(["today"], Ids(removed.View!));
        Assert.False((await service.RemoveAsync(Owner, "finance", null, default)).Succeeded);
        Assert.False((await service.RemoveAsync(Owner, "today", null, default)).Succeeded);

        var hidden = await service.SetCardVisibleAsync(Owner, "Morning brief", false, null, default);
        Assert.Equal(["brief"], hidden.View!.HiddenCards);
        Assert.False((await service.SetCardVisibleAsync(Owner, "needs-you", false, null, default)).Succeeded);

        var undone = await service.UndoAsync(Owner, default);
        Assert.Empty(undone.View!.HiddenCards);
        Assert.Equal(HomeLayoutAuthors.App, undone.View.ChangedBy);
        var redone = await service.UndoAsync(Owner, default);
        Assert.Equal(["brief"], redone.View!.HiddenCards);

        var reset = await service.ResetAsync(Owner, HomeLayoutAuthors.Jarvis, default);
        Assert.Equal(HomeLayoutRules.DefaultTiles, reset.View!.Tiles);
        Assert.Empty(reset.View.HiddenCards);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_is_not_saved_again()
    {
        var (service, events) = Create();
        var first = await service.PinAsync(Owner, "today", "wide", 1, null, default);
        var again = await service.PinAsync(Owner, "today", "wide", 1, null, default);

        Assert.False(again.Changed);
        Assert.Equal(first.View!.Revision, again.View!.Revision);
        Assert.Single(events);
    }

    [Fact]
    public async Task Connected_app_tiles_the_app_saved_can_be_moved_by_their_app_name_but_never_invented()
    {
        var (service, _) = Create();
        const string notion = "app:3f2a~Notion";
        await service.SaveFromAppAsync(Owner, [new("today", "wide"), new(notion, "large")], null, default);
        Assert.Equal("wide", (await service.GetAsync(Owner, default)).Tiles[1].Size);

        var moved = await service.PinAsync(Owner, "notion", "square", 1, null, default);
        Assert.Equal(new HomeLayoutTile(notion, "square"), moved.View!.Tiles[0]);
        Assert.False((await service.PinAsync(Owner, "app:zzz~Slack", null, null, null, default)).Succeeded);
    }

    [Fact]
    public void The_stored_state_survives_the_settings_store_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var state = new HomeLayoutState([new("today", "wide")], ["brief"], 4, HomeLayoutAuthors.Jarvis,
            DateTimeOffset.UnixEpoch, "Why", new HomeLayoutSnapshot([new("tasks", "square")], []));

        var back = JsonSerializer.Deserialize<HomeLayoutState>(JsonSerializer.Serialize(state, options), options)!;

        Assert.Equal(state.Tiles, back.Tiles);
        Assert.Equal(state.HiddenCards, back.HiddenCards);
        Assert.Equal(state.Previous!.Tiles, back.Previous!.Tiles);
        Assert.Equal(4, back.Revision);
    }

    [Fact]
    public async Task The_tools_describe_home_and_only_read_it_in_background_tasks()
    {
        var (service, _) = Create();
        var tools = new HomeLayoutAgentTools(service, new User());

        var layout = await tools.GetHomeLayoutAsync();
        Assert.Contains("today \"Today\" wide", layout);
        Assert.Contains("weekly-review \"Weekly review\"", layout);
        Assert.Contains("brief: Morning brief", layout);

        var pinned = await tools.PinHomeTileAsync("habits", "wide", 1, "Habits first");
        Assert.StartsWith("Home now shows: Habits (wide)", pinned);
        Assert.Contains("I could not change Home", await tools.RemoveHomeTileAsync("teleporter"));
        Assert.Contains("Jarvis made the latest change (Habits first)", await tools.GetHomeLayoutAsync());

        var contributor = new HomeLayoutToolContributor(service, new User());
        Assert.Equal(7, contributor.GetTools(new Jarvis.Agents.AgentBuildContext(Owner, null)).Count());
        Assert.Equal(["GetHomeLayout"],
            contributor.GetTools(new Jarvis.Agents.AgentBuildContext(Owner, Guid.NewGuid())).Select(t => t.Name));
    }
}
