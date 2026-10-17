using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.People;
using Jarvis.Application.Search;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.People;
using Jarvis.Infrastructure.Search;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class LifeSearchProviderTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly DateTimeOffset At = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static Person Someone(string name, string? relationship = null, string? notes = null,
        DateTimeOffset? contacted = null) =>
        new(Guid.CreateVersion7(), Owner, name, relationship, null, null, null, notes, null, contacted, null, null,
            null, At, At);

    private static Commitment Promise(string description, string who = "Rosa", bool suggested = false,
        string status = CommitmentStatuses.Open, string direction = CommitmentDirections.IOwe, DateOnly? due = null) =>
        new(Guid.CreateVersion7(), Owner, direction, who, description, due, status, suggested, CommitmentSources.Chat,
            null, null, null, At, At);

    private static DecisionView Choice(string title, string prediction = "It works", string status = "open",
        string? context = null, bool? outcome = null) =>
        new(Guid.CreateVersion7(), title, context, prediction, .7, new DateOnly(2026, 12, 1), status, outcome, null,
            null, At);

    private static PersonSearchProvider People(params Person[] people) =>
        new(Fake<IPeopleService>.Create(("ListAsync", _ => (IReadOnlyList<Person>)people)));

    private static CommitmentSearchProvider Promises(params Commitment[] promises) =>
        new(Fake<ICommitmentService>.Create(("ListAsync", _ => (IReadOnlyList<Commitment>)promises)));

    private static DecisionSearchProvider Decisions(params DecisionView[] decisions) =>
        new(Fake<IDecisionService>.Create(("ListAsync", _ => (IReadOnlyList<DecisionView>)decisions)));

    [Fact]
    public async Task People_are_found_by_name_without_accents_and_open_as_a_person_route()
    {
        var joel = Someone("Joël Visser", "colleague");
        var hits = await People(joel, Someone("Rosa")).SearchAsync(Owner, "joel", 8, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal(SearchResultKinds.Person, hit.Kind);
        Assert.Equal("colleague", hit.Summary);
        Assert.Equal("person", hit.Route.Kind);
        Assert.Equal(joel.Id.ToString(), hit.Route.Parameters["personId"]);
    }

    [Fact]
    public async Task A_person_matched_only_by_notes_never_shows_the_note()
    {
        var hits = await People(Someone("Tom", null, "allergic to peanuts"))
            .SearchAsync(Owner, "peanuts", 8, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("Person", hit.Summary);
        Assert.DoesNotContain("peanuts", hit.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Promises_say_who_promised_what_and_whether_they_are_accepted()
    {
        var accepted = Promise("Send the tile quotes", due: new DateOnly(2026, 11, 3));
        var suggested = Promise("Send the tile photos", suggested: true);
        var theirs = Promise("Share the invoice", "Tom", direction: CommitmentDirections.OwedToMe);

        var hits = await Promises(accepted, suggested, theirs)
            .SearchAsync(Owner, "send", 8, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, hit => hit.Summary == "You promised Rosa · Due 2026-11-03");
        Assert.Contains(hits, hit => hit.Summary == "You promised Rosa · Suggested, not accepted yet");
        var byWho = await Promises(theirs).SearchAsync(Owner, "tom", 8, CancellationToken.None);
        Assert.Equal("Tom promised you · Open", Assert.Single(byWho).Summary);
        Assert.Equal("commitment", byWho[0].Route.Kind);
    }

    [Fact]
    public async Task Decisions_are_found_by_title_prediction_or_background_and_show_their_state()
    {
        var open = Choice("Switch bank", "Fees drop by half");
        var due = Choice("Hire help", status: "due", context: "Too much admin");
        var done = Choice("Buy the bike", "It lasts", "resolved", outcome: true);

        var provider = Decisions(open, due, done);
        Assert.Equal("Fees drop by half · Review 2026-12-01",
            Assert.Single(await provider.SearchAsync(Owner, "fees", 8, CancellationToken.None)).Summary);
        Assert.EndsWith("Time to review",
            Assert.Single(await provider.SearchAsync(Owner, "admin", 8, CancellationToken.None)).Summary);
        Assert.EndsWith("Happened",
            Assert.Single(await provider.SearchAsync(Owner, "bike", 8, CancellationToken.None)).Summary);
        Assert.Equal("decision", (await provider.SearchAsync(Owner, "bike", 8, CancellationToken.None))[0].Route.Kind);
    }

    [Fact]
    public async Task Nothing_found_is_an_empty_list_for_every_kind()
    {
        Assert.Empty(await People(Someone("Rosa")).SearchAsync(Owner, "zzz", 8, CancellationToken.None));
        Assert.Empty(await Promises(Promise("Call")).SearchAsync(Owner, "zzz", 8, CancellationToken.None));
        Assert.Empty(await Decisions(Choice("Move")).SearchAsync(Owner, "zzz", 8, CancellationToken.None));
    }

    [Fact]
    public async Task The_best_hits_survive_the_limit_whatever_order_they_were_listed_in()
    {
        var weak = Promise("Please send it all to Rosa again");
        var exact = Promise("Send");
        var prefix = Promise("Send the quotes");

        foreach (var order in new[] { new[] { weak, prefix, exact }, new[] { exact, prefix, weak }, new[] { prefix, weak, exact } })
        {
            var hits = await Promises(order).SearchAsync(Owner, "send", 2, CancellationToken.None);
            Assert.Equal(["Send", "Send the quotes"], hits.Select(hit => hit.Title));
        }
    }

    [Theory]
    [InlineData("Café Müller", "cafe muller")]
    [InlineData("Zoë", "ZOE")]
    [InlineData("naïve", "naive")]
    public void Accents_do_not_stop_a_match(string text, string query)
    {
        Assert.True(SearchRanking.MatchesQuery(text, SearchRanking.NormalizeQuery(query)));
        Assert.True(SearchRanking.TextRelevance(text, query) > 0);
    }

    [Fact]
    public void The_new_kinds_are_valid_filters()
    {
        Assert.True(SearchResultKinds.IsValidFilter(new HashSet<string> { "person", "commitment", "decision" }));
    }
}
