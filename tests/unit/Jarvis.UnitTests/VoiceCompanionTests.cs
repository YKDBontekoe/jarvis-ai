using System.Text.Json;
using Jarvis.Application.Comparisons;
using Jarvis.Application.Library;
using Jarvis.Application.Opportunities;
using Jarvis.Application.Voice;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class VoiceCompanionTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fb01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fb02");
    private static readonly Guid Conversation = Guid.Parse("01996b8c-6000-7000-8000-00000000fb10");
    private static readonly Guid ComparisonId = Guid.Parse("01996b8c-6000-7000-8000-00000000fb20");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Time = Now;
        public override DateTimeOffset GetUtcNow() => Time;
    }

    private static Opportunity Opportunity(string title) => new($"k-{title}", "follow_up", "g", title, $"Why {title}", [],
        "commitments", 2, DateOnly.FromDateTime(Now.UtcDateTime), []);

    private static ComparisonView Comparison()
    {
        var definition = new ComparisonDefinition("Which kettle?", null,
            [new ComparisonCriterion("p", "Price", 8)],
            [
                new ComparisonOption("a", "Quick", null, new Dictionary<string, ComparisonCell> { ["p"] = new(9) }),
                new ComparisonOption("b", "Slow", null, new Dictionary<string, ComparisonCell> { ["p"] = new(4) })
            ]);
        return new ComparisonView(new ComparisonRecord(ComparisonId, Owner, definition, null, null, Now, Now),
            ComparisonCalculator.Evaluate(definition));
    }

    private sealed class World
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly Clock Time = new();
        public readonly List<NoteDraft> Saved = [];
        public int Opportunities = 2;

        public VoiceCompanionService Service => new(Settings,
            Fake<IOpportunityService>.Create(("ListAsync", _ => new OpportunityList(
                Enumerable.Range(1, Opportunities).Select(i => Opportunity($"Item {i}")).ToArray(), [], []))),
            Fake<IComparisonService>.Create(("GetAsync", args => (Guid)args[0]! == ComparisonId && (Guid)args[1]! == Owner ? Comparison() : null)),
            Fake<ILibraryService>.Create(("AddNoteAsync", args =>
            {
                Saved.Add((NoteDraft)args[1]!);
                return LibraryOperation<LibraryItem>.Ok(new LibraryItem(Guid.NewGuid(), Owner, "report", null, "t", "", [], [], "c", "app", null, Now, Now));
            })),
            Time);
    }

    [Fact]
    public async Task What_needs_you_gets_a_card_that_names_the_same_items_the_voice_summarises()
    {
        var world = new World();

        var card = await world.Service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default);

        Assert.NotNull(card);
        Assert.Equal(CompanionKinds.NeedsYou, card.Kind);
        Assert.Equal(["Item 1", "Item 2"], card.Items.Select(i => i.Title));
        Assert.Equal("commitments", card.Items[0].Destination);
        Assert.Contains("2 things worth your attention; the first is Item 1", card.Spoken);
        Assert.Equal(CompanionStates.Shown, card.State);
    }

    [Fact]
    public async Task A_comparison_card_ranks_the_options_and_does_not_overstate_a_lead()
    {
        var world = new World();

        var card = await world.Service.CardForToolAsync(Owner, Conversation, "GetComparison",
            JsonSerializer.Serialize(new { comparisonId = ComparisonId }), default);

        Assert.NotNull(card);
        Assert.Equal("Which kettle?", card.Title);
        Assert.Equal(["Quick", "Slow"], card.Items.Select(i => i.Title));
        Assert.Equal($"comparison:{ComparisonId}", card.Items[0].Destination);
        Assert.Contains("Quick leads, but check what is still unknown", card.Spoken);
    }

    [Fact]
    public async Task Other_tools_unknown_comparisons_and_other_owners_get_no_card()
    {
        var world = new World();
        var service = world.Service;

        Assert.Null(await service.CardForToolAsync(Owner, Conversation, "GetClock", "{}", default));
        Assert.Null(await service.CardForToolAsync(Owner, Conversation, "GetComparison", "{}", default));
        Assert.Null(await service.CardForToolAsync(Owner, Conversation, "GetComparison", "not json", default));
        Assert.Null(await service.CardForToolAsync(Other, Conversation, "GetComparison",
            JsonSerializer.Serialize(new { comparisonId = ComparisonId }), default));
        Assert.Empty(await service.ListAsync(Owner, Conversation, default));
    }

    [Fact]
    public async Task Asking_the_same_thing_twice_replaces_the_open_card_instead_of_stacking()
    {
        var world = new World();
        var service = world.Service;

        await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default);
        await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default);

        Assert.Single(await service.ListAsync(Owner, Conversation, default));
    }

    [Fact]
    public async Task Save_that_means_the_only_card_just_shown_and_writes_one_private_note_once()
    {
        var world = new World();
        var service = world.Service;
        var card = (await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default))!;

        var reference = await service.ResolveLastAsync(Owner, Conversation, null, default);
        var first = await service.SaveAsync(Owner, reference.Card!.Id, default);
        var second = await service.SaveAsync(Owner, card.Id, default);

        Assert.Equal(card.Id, reference.Card.Id);
        Assert.True(first.Succeeded);
        Assert.Equal(CompanionStates.Saved, first.Value!.State);
        Assert.Single(world.Saved);
        Assert.Contains("private", world.Saved[0].Tags!);
        Assert.Contains("voice", world.Saved[0].Tags!);
        Assert.Contains("Item 1: Why Item 1", world.Saved[0].Content);
        Assert.Equal(first.Value.LibraryItemId, second.Value!.LibraryItemId);
        Assert.Empty((await service.ResolveLastAsync(Owner, Conversation, null, default)).Choices);
        Assert.Null((await service.ResolveLastAsync(Owner, Conversation, null, default)).Card);
    }

    [Fact]
    public async Task With_two_recent_cards_it_asks_instead_of_guessing_and_a_name_settles_it()
    {
        var world = new World();
        var service = world.Service;
        await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default);
        await service.CardForToolAsync(Owner, Conversation, "GetComparison",
            JsonSerializer.Serialize(new { comparisonId = ComparisonId }), default);

        var unclear = await service.ResolveLastAsync(Owner, Conversation, null, default);
        var named = await service.ResolveLastAsync(Owner, Conversation, "kettle", default);

        Assert.Null(unclear.Card);
        Assert.Equal(["What needs you", "Which kettle?"], unclear.Choices.Select(c => c.Title));
        Assert.Equal("Which kettle?", named.Card!.Title);
        Assert.Empty(world.Saved);
    }

    [Fact]
    public async Task An_old_card_is_no_longer_what_that_means()
    {
        var world = new World();
        var service = world.Service;
        await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default);

        world.Time.Time = Now.AddMinutes(10);

        Assert.Null((await service.ResolveLastAsync(Owner, Conversation, null, default)).Card);
    }

    [Fact]
    public async Task A_dismissed_card_cannot_be_saved_and_stays_dismissed()
    {
        var world = new World();
        var service = world.Service;
        var card = (await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default))!;

        var dismissed = await service.DismissAsync(Owner, card.Id, default);
        var save = await service.SaveAsync(Owner, card.Id, default);
        var again = await service.DismissAsync(Owner, card.Id, default);

        Assert.Equal(CompanionStates.Dismissed, dismissed.Value!.State);
        Assert.False(save.Succeeded);
        Assert.Contains("put that card away", save.Message);
        Assert.Equal(CompanionStates.Dismissed, again.Value!.State);
        Assert.Empty(world.Saved);
        Assert.Null((await service.ResolveLastAsync(Owner, Conversation, null, default)).Card);
    }

    [Fact]
    public async Task The_recap_keeps_saved_dismissed_and_open_apart_and_claims_nothing_was_executed()
    {
        var world = new World();
        var service = world.Service;
        var needs = (await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default))!;
        var comparison = (await service.CardForToolAsync(Owner, Conversation, "GetComparison",
            JsonSerializer.Serialize(new { comparisonId = ComparisonId }), default))!;
        await service.SaveAsync(Owner, needs.Id, default);
        await service.CardForToolAsync(Owner, Other == Owner ? Conversation : Guid.NewGuid(), "GetOpportunities", "{}", default);

        var recap = await service.RecapAsync(Owner, Conversation, default);

        Assert.Equal(2, recap.Shown);
        Assert.Equal(1, recap.Saved);
        Assert.Equal(1, recap.Open);
        Assert.Equal(0, recap.Dismissed);
        Assert.Equal("Saved to your Library.", recap.Lines.Single(l => l.CardId == needs.Id).Text);
        Assert.Equal("Still open: not saved.", recap.Lines.Single(l => l.CardId == comparison.Id).Text);
        Assert.Contains("Nothing was sent, accepted or changed outside Jarvis by voice", recap.Statement);
    }

    [Fact]
    public async Task One_owner_cannot_save_or_dismiss_another_owners_card()
    {
        var world = new World();
        var card = (await world.Service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default))!;

        var save = await world.Service.SaveAsync(Other, card.Id, default);
        var dismiss = await world.Service.DismissAsync(Other, card.Id, default);

        Assert.True(save.NotFound);
        Assert.False(dismiss.Succeeded);
        Assert.Empty(world.Saved);
    }

    [Fact]
    public async Task The_voice_tools_ask_when_unclear_and_recap_in_plain_words()
    {
        var world = new World();
        var service = world.Service;
        var tools = new Jarvis.Agents.Voice.VoiceCompanionTools(service, new FixedUser(), Conversation);

        Assert.Equal("No card is waiting to be saved. Nothing was saved.", await tools.SaveShownCardAsync());
        await service.CardForToolAsync(Owner, Conversation, "GetOpportunities", "{}", default);
        await service.CardForToolAsync(Owner, Conversation, "GetComparison",
            JsonSerializer.Serialize(new { comparisonId = ComparisonId }), default);
        var ask = await tools.SaveShownCardAsync();
        Assert.Contains("\"What needs you\" or \"Which kettle?\"", ask);
        Assert.Contains("Nothing was saved", ask);

        var saved = await tools.SaveShownCardAsync("kettle");
        Assert.Contains("Saved \"Which kettle?\"", saved);
        var recap = await tools.GetSessionRecapAsync();
        Assert.Contains("2 shown, 1 saved, 0 put away, 1 still open", recap);
    }

    private sealed class FixedUser : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
