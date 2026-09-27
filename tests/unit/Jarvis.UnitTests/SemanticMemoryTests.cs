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
            ("SearchTextAsync", _ => (IReadOnlyList<MemoryRecord>)[keyword]),
            ("SearchTrigramAsync", _ => (IReadOnlyList<MemoryRecord>)[]));
        string? searchedModel = null;
        var index = Fake<IMemoryIndexRepository>.Create(("SearchSemanticAsync", args =>
        {
            searchedModel = ((MemoryEmbedding)args[1]!).Model;
            return (IReadOnlyList<MemoryRecord>)[semanticOnly, keyword];
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
            ("SearchTextAsync", _ => (IReadOnlyList<MemoryRecord>)[keyword]),
            ("SearchTrigramAsync", _ => (IReadOnlyList<MemoryRecord>)[]));
        var embedder = Fake<IMemoryEmbedder>.Create(("EmbedAsync", _ =>
            Task.FromException<IReadOnlyList<MemoryEmbedding>?>(new HttpRequestException("provider down"))));

        var hits = await new MemoryService(repository, Fake<IMemoryIndexRepository>.Create(), embedder)
            .SearchAsync(Owner, "window", default);

        Assert.Equal(keyword.Id, Assert.Single(hits).Memory.Id);
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
