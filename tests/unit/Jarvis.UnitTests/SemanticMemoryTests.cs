using Jarvis.Agents.Memory;
using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;
using Jarvis.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SemanticMemoryTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly DateTimeOffset Recorded = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Search_fuses_semantic_hits_that_keyword_search_missed()
    {
        var keyword = Memory("The user prefers window seats.");
        var semanticOnly = Memory("The user loves sourdough baking.");
        var repository = Fake<IMemoryRepository>.Create(
            ("HasActiveMemoriesAsync", _ => true),
            ("SearchLexicalAsync", _ => (IReadOnlyList<MemoryLexicalMatch>)[new(keyword, 1, 0)]));
        string? searchedModel = null;
        var index = Fake<IMemoryIndexRepository>.Create(("SearchSemanticAsync", args =>
        {
            searchedModel = ((MemoryEmbedding)args[1]!).Model;
            return (IReadOnlyList<MemorySearchHit>)[new(semanticOnly, 0.72), new(keyword, 0.7)];
        }));
        var embedder = Fake<IMemoryEmbedder>.Create(("EmbedAsync", _ =>
            (IReadOnlyList<MemoryEmbedding>?)[new MemoryEmbedding("openrouter:embed", [1f, 0f])]));

        var hits = await new MemoryService(repository, index, embedder).SearchAsync(Owner, "bread hobby", default);

        Assert.Equal("openrouter:embed", searchedModel);
        Assert.Equal(keyword.Id, hits[0].Memory.Id);
        Assert.Contains(hits, hit => hit.Memory.Id == semanticOnly.Id);
    }

    [Fact]
    public async Task Search_falls_back_to_keywords_when_embeddings_fail()
    {
        var keyword = Memory("The user prefers window seats.");
        var repository = Fake<IMemoryRepository>.Create(
            ("HasActiveMemoriesAsync", _ => true),
            ("SearchLexicalAsync", _ => (IReadOnlyList<MemoryLexicalMatch>)[new(keyword, 1, 0)]));
        var embedder = Fake<IMemoryEmbedder>.Create(("EmbedAsync", _ =>
            Task.FromException<IReadOnlyList<MemoryEmbedding>?>(new HttpRequestException("provider down"))));

        var hits = await new MemoryService(repository, Fake<IMemoryIndexRepository>.Create(), embedder)
            .SearchAsync(Owner, "window", default);

        Assert.Equal(keyword.Id, Assert.Single(hits).Memory.Id);
    }

    [Fact]
    public async Task Search_without_embeddings_skips_the_existence_check_and_sends_or_terms()
    {
        MemoryQuery? sent = null;
        var repository = Fake<IMemoryRepository>.Create(("SearchLexicalAsync", args =>
        {
            sent = (MemoryQuery)args[1]!;
            return (IReadOnlyList<MemoryLexicalMatch>)[];
        }));

        Assert.Empty(await new MemoryService(repository).SearchAsync(Owner, "Hoe heet mijn zus ook alweer?", default));

        Assert.Equal(["zus", "sister"], sent!.Terms.Select(term => term.Term));
        Assert.Equal([1d, 0.5], sent.Terms.Select(term => term.Weight));
    }

    [Theory]
    [InlineData("Where does my sister work?", "sister:*|work:*|zus|werk:*|job")]
    [InlineData("Is Anna still training for the marathon?", "anna:*|train:*|marathon:*|trein:*")]
    [InlineData("Waar woon ik?", "woon:*|live:*")]
    [InlineData("the and of mijn", "")]
    public void Query_parsing_drops_stopwords_stems_and_adds_translations(string text, string expected)
    {
        var query = MemoryQuery.Parse(text);

        Assert.Equal(expected, string.Join('|', query.Terms.Select(term => term.TsQuery)));
        Assert.All(query.Terms, term => Assert.Matches("^[\\p{L}\\p{N}]+(:\\*)?$", term.TsQuery));
    }

    [Fact]
    public void Query_terms_are_safe_tsquery_lexemes_for_hostile_input()
    {
        var query = MemoryQuery.Parse("x' | !(y) & z:* <-> 'drop table memories; -- ümlaut café");

        Assert.All(query.Terms, term => Assert.Matches("^[\\p{L}\\p{N}]+(:\\*)?$", term.TsQuery));
        Assert.Contains(query.Terms, term => term.Term == "café");
        Assert.True(query.Terms.Count <= MemoryQuery.MaxTerms);
    }

    [Fact]
    public void Ranking_prefers_relevance_then_importance_recency_and_use()
    {
        var now = Recorded.AddDays(10);
        var strong = Memory("The user's sister is Anna.") with { Importance = 0.2f };
        var weakButImportant = Memory("Anna likes tea.") with { Importance = 1f, AccessCount = 20 };
        var fresh = Memory("Anna moved to Delft.") with { UpdatedAt = now };
        var stale = Memory("Anna moved to Leiden.") with { UpdatedAt = now.AddDays(-400) };

        var hits = MemoryRanking.Rank(
            [new(strong, 1, 0), new(weakButImportant, 0.5, 0), new(stale, 0.6, 0), new(fresh, 0.6, 0)], [], now);

        // Relevance dominates; between equally relevant memories the fresher one wins, and importance plus use can
        // lift a less relevant memory above a stale one.
        Assert.Equal([strong.Id, fresh.Id, weakButImportant.Id, stale.Id], hits.Select(hit => hit.Memory.Id));
    }

    [Fact]
    public void Ranking_cuts_the_weak_tail_and_skips_near_duplicates()
    {
        var top = Memory("The user is vegetarian and does not eat fish.");
        var copy = Memory("The user is vegetarian and does not eat fish!");
        var related = Memory("The user hates cilantro.");
        var noise = Memory("The user has a monstera.");

        var hits = MemoryRanking.Rank([new(top, 1, 0), new(copy, 0.95, 0), new(related, 0.5, 0), new(noise, 0.1, 0)],
            [], Recorded);

        Assert.Equal([top.Id, related.Id], hits.Select(hit => hit.Memory.Id));
    }

    [Fact]
    public void Ranking_rewards_agreement_between_keyword_and_semantic_hits()
    {
        var both = Memory("The user bakes sourdough on Sundays.");
        var keywordOnly = Memory("The user bought sourdough flour.");
        var semanticOnly = Memory("The user enjoys baking bread.");

        var hits = MemoryRanking.Rank([new(both, 0.6, 0), new(keywordOnly, 0.6, 0)],
            [new(both, 0.6), new(semanticOnly, 0.7)], Recorded);

        Assert.Equal(both.Id, hits[0].Memory.Id);
        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public void Graph_extraction_maps_facts_to_memories_and_rejects_bad_items()
    {
        var first = Memory("The user moved to Amsterdam in 2026.");
        var second = Memory("Anna's birthday is 14 March.");
        var facts = KnowledgeGraphExtractor.Parse("""
            Here you go: {"facts":[
              {"memory":0,"subject":"user","subjectType":"person","predicate":"lives in","object":"Amsterdam",
               "objectType":"place","objectIsEntity":true,"exclusive":true,"validFrom":"2026-01-15"},
              {"memory":1,"subject":"Anna","subjectType":"person","predicate":"birthday","object":"14 March",
               "objectIsEntity":false,"exclusive":true,"validFrom":null},
              {"memory":1,"subject":"Anna","predicate":"token","object":"ghp_abcdefghijklmnopqrstuvwxyz0123456789"},
              {"memory":7,"subject":"ghost","predicate":"haunts","object":"nothing"}
            ]}
            """, [first, second]);

        var moved = Assert.Single(facts[first.Id]);
        Assert.True(moved.Exclusive);
        Assert.Equal("place", moved.ObjectType);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), moved.ValidFrom.ToUniversalTime());
        var birthday = Assert.Single(facts[second.Id]);
        Assert.False(birthday.ObjectIsEntity);
        Assert.Equal(Recorded, birthday.ValidFrom);
    }

    [Theory]
    [InlineData("Me", "user")]
    [InlineData("the user", "user")]
    [InlineData("  St. Mary's  ", "stmarys")]
    public void Graph_names_normalize_to_stable_keys(string name, string key)
    {
        Assert.Equal(key, GraphNames.Key(name));
    }

    [Fact]
    public void Graph_predicates_become_snake_case()
    {
        Assert.Equal("works_at", GraphNames.Predicate(" Works-At "));
        Assert.Equal("lives_in", GraphNames.Predicate("lives in"));
    }

    [Fact]
    public void Rendering_marks_current_and_historical_facts()
    {
        var you = Guid.NewGuid();
        var details = new GraphEntityDetails(
            new GraphEntityRecord(you, "You", "person", null, [], 1, Recorded),
            [new GraphRelationRecord(Guid.NewGuid(), you, "You", "lives_in", Guid.NewGuid(), "Amsterdam", null,
                Recorded.AddMonths(2), null, 0.9f, null)],
            [new GraphRelationRecord(Guid.NewGuid(), you, "You", "lives_in", Guid.NewGuid(), "Utrecht", null,
                Recorded, Recorded.AddMonths(2), 0.9f, null)]);

        var text = KnowledgeGraphTools.Render(details);

        Assert.Contains("- You lives in Amsterdam", text);
        Assert.Contains("You lives in Utrecht (2026-03-01 → 2026-05-01)", text);
    }

    private static MemoryRecord Memory(string content) => new(Guid.NewGuid(), Owner, "fact", content, 0.5f, 0.9f,
        "user", null, Recorded, Recorded, null, false);
}
