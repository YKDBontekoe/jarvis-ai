using Jarvis.Agents.Memory;
using Jarvis.Application.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class GraphQuestionTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly DateTimeOffset Then = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid You = Id(1), Rosa = Id(2), Arjan = Id(3), Philips = Id(4), Eindhoven = Id(5),
        Gym = Id(6), Celia = Id(7);

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    private static GraphEntityRecord Entity(Guid id, string name, string type, params string[] aliases) =>
        new(id, name, type, null, aliases, 1, Then);

    private static GraphEdge Edge(Guid from, string predicate, Guid to) => new(from, to, predicate, 0.9f, Then);

    private static FakeGraph Graph() => new(new GraphOverview(
        [
            Entity(You, "You", "person"), Entity(Rosa, "Rosa", "person"), Entity(Arjan, "Arjan Bontekoe", "person"),
            Entity(Philips, "Philips", "organization"), Entity(Eindhoven, "Eindhoven", "place"),
            Entity(Gym, "Gym", "place"), Entity(Celia, "Celia", "person", "Cee")
        ],
        [
            Edge(You, "knows", Rosa), Edge(You, "works_at", Philips), Edge(You, "trains_at", Gym),
            Edge(Rosa, "married_to", Arjan), Edge(Arjan, "works_at", Philips), Edge(Philips, "based_in", Eindhoven),
            Edge(You, "knows", Celia)
        ],
        [
            new GraphLiteral(Celia, "birthday", "12 June", 0.9f, Then),
            new GraphLiteral(You, "job_title", "designer", 0.9f, Then)
        ]));

    [Fact]
    public async Task Named_entities_their_path_and_cited_links_reach_the_answer()
    {
        var model = new FakeModel(facts => new GraphModelAnswer("Rosa is married to Arjan, who works at Philips.",
            facts.Where(f => f.Predicate is "married_to" || (f.Predicate == "works_at" && f.SubjectId == Arjan))
                .Select(f => f.Id).ToArray(), ["Where is Philips based?"]));
        var service = new GraphQuestionService(Graph(), model);

        var answer = await service.AskAsync(Owner, "How is Rosa connected to Philips?", [], [], default);

        Assert.True(answer.Grounded);
        // The path goes Rosa → Arjan → Philips, not through "You", even though that is just as short.
        Assert.Contains(model.Facts!, f => f.Sentence == "Rosa married to Arjan Bontekoe");
        Assert.Contains(model.Facts!, f => f.Sentence == "Arjan Bontekoe works at Philips");
        Assert.Equal(new[] { Rosa, Arjan, Philips }.Order(), answer.EntityIds.Order());
        Assert.Contains(new GraphAnswerLink(Rosa, Arjan, "married_to"), answer.Links);
        Assert.Equal(["Where is Philips based?"], answer.FollowUps);
    }

    [Fact]
    public async Task My_and_wording_matches_pull_in_the_user_and_matching_details()
    {
        var model = new FakeModel(_ => new GraphModelAnswer("Celia, on 12 June.", [], []));
        var service = new GraphQuestionService(Graph(), model);

        var answer = await service.AskAsync(Owner, "Whose birthday is coming up, and what is my job?", [], [], default);

        Assert.Contains(model.Facts!, f => f.Sentence == "Celia birthday 12 June");
        Assert.Contains(model.Facts!, f => f.Sentence == "You job title designer");
        // Nothing cited: the seeds stand in so the map still has something to show.
        Assert.Contains(You, answer.EntityIds);
    }

    [Fact]
    public async Task Focus_aliases_and_history_feed_retrieval()
    {
        var model = new FakeModel(_ => new GraphModelAnswer("ok", [], []));
        var service = new GraphQuestionService(Graph(), model);

        await service.AskAsync(Owner, "And where do they work?", [Gym],
            [new GraphQuestionTurn("Tell me about Cee", "Celia was born on 12 June.")], default);

        Assert.Contains(model.Facts!, f => f.Sentence == "You trains at Gym");
        Assert.Contains(model.Facts!, f => f.Sentence == "Celia birthday 12 June");
        Assert.Single(model.History!);
    }

    [Fact]
    public async Task Without_a_model_the_answer_lists_the_facts_found()
    {
        var service = new GraphQuestionService(Graph(), new FakeModel(_ => null));

        var answer = await service.AskAsync(Owner, "What about Arjan Bontekoe?", [], [], default);

        Assert.False(answer.Grounded);
        Assert.Contains("Arjan Bontekoe works at Philips", answer.Answer);
        Assert.Contains(Arjan, answer.EntityIds);
        Assert.Contains(new GraphAnswerLink(Arjan, Philips, "works_at"), answer.Links);
    }

    [Fact]
    public async Task An_empty_graph_says_so_without_calling_the_model()
    {
        var model = new FakeModel(_ => throw new InvalidOperationException("not called"));
        var service = new GraphQuestionService(new FakeGraph(new GraphOverview([], [], [])), model);

        var answer = await service.AskAsync(Owner, "Who do I know?", [], [], default);

        Assert.False(answer.Grounded);
        Assert.Null(model.Facts);
    }

    [Theory]
    [InlineData("""{"answer":"Rosa.","facts":[1,"2",-4],"followUps":["Who else?"]}""", "Rosa.", 2, 1)]
    [InlineData("""Sure! {"answer":"Hi","facts":"x"} done""", "Hi", 0, 0)]
    public void Model_answers_are_parsed_leniently(string text, string answer, int facts, int followUps)
    {
        var parsed = ModelGraphAnswerModel.Parse(text)!;
        Assert.Equal(answer, parsed.Answer);
        Assert.Equal(facts, parsed.FactIds.Count);
        Assert.Equal(followUps, parsed.FollowUps.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no json")]
    [InlineData("""{"answer":""}""")]
    public void Unusable_model_answers_are_rejected(string? text) => Assert.Null(ModelGraphAnswerModel.Parse(text));

    private sealed class FakeModel(Func<IReadOnlyList<GraphContextFact>, GraphModelAnswer?> answer) : IGraphAnswerModel
    {
        public IReadOnlyList<GraphContextFact>? Facts { get; private set; }
        public IReadOnlyList<GraphQuestionTurn>? History { get; private set; }

        public Task<GraphModelAnswer?> AnswerAsync(Guid ownerId, string question,
            IReadOnlyList<GraphQuestionTurn> history, IReadOnlyList<GraphContextFact> facts, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Facts = facts;
            History = history;
            return Task.FromResult(answer(facts));
        }
    }

    private sealed class FakeGraph(GraphOverview overview) : IKnowledgeGraphRepository
    {
        public Task<GraphOverview> GetOverviewAsync(Guid ownerId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(overview);

        public Task<GraphEntityDetails?> GetEntityAsync(Guid ownerId, Guid entityId,
            CancellationToken cancellationToken) =>
            Task.FromResult<GraphEntityDetails?>(null);

        public Task<IReadOnlyList<GraphEntityRecord>> ListEntitiesAsync(Guid ownerId, string? search, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GraphEntityDetails?> FindEntityAsync(Guid ownerId, string name, DateTimeOffset? asOf,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<GraphEntityRecord>> FindMentionedAsync(Guid ownerId, string text, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> MergeAsync(Guid ownerId, IReadOnlyList<GraphFact> facts, Guid? sourceMemoryId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GraphEntityRecord?> UpdateEntityAsync(Guid ownerId, Guid entityId, string? name, string? type,
            string? summary, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CloseRelationAsync(Guid ownerId, Guid relationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> MergeEntitiesAsync(Guid ownerId, Guid keepId, Guid absorbId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
