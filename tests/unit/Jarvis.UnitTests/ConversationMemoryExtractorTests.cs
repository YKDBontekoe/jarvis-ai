using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Audit;
using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConversationMemoryExtractorTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000e0de");

    private static MemoryRecord Memory(string kind, string content, bool pinned = false, float importance = 0.5f) =>
        new(Guid.NewGuid(), Owner, kind, content, importance, 0.9f, "conversation", null, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, null, pinned);

    private sealed class Harness
    {
        public List<MemoryRecord> Stored { get; } = [];
        public List<(Guid Target, string Content, float Importance)> Replaced { get; } = [];
        public List<Guid> Expired { get; } = [];
        public List<string> Audit { get; } = [];
        public List<string> Searches { get; } = [];
        public string? Prompt { get; private set; }

        public async Task RunAsync(string reply, string userMessage, IReadOnlyList<MemoryRecord> existing,
            IReadOnlyList<MemoryExtractionTurn>? context = null)
        {
            var memories = Fake<IMemoryService>.Create(
                ("ListAsync", _ => existing),
                ("SearchAsync", args =>
                {
                    Searches.Add((string)args[1]!);
                    return (IReadOnlyList<MemorySearchHit>)existing.Select(memory => new MemorySearchHit(memory, 1))
                        .ToArray();
                }),
                ("CreateAsync", args =>
                {
                    var record = Memory((string)args[1]!, (string)args[2]!);
                    Stored.Add(record);
                    return record;
                }),
                ("ReplaceAsync", args =>
                {
                    Replaced.Add(((Guid)args[0]!, (string)args[3]!, (float)args[4]!));
                    return Memory((string)args[2]!, (string)args[3]!);
                }),
                ("ExpireAsync", args =>
                {
                    var id = (Guid)args[0]!;
                    Expired.Add(id);
                    return existing.First(memory => memory.Id == id) with { ValidUntil = DateTimeOffset.UtcNow };
                }));
            var resolver = Fake<IChatClientResolver>.Create(("GetChatClientAsync", _ => (IChatClient)new Replying(reply,
                prompt => Prompt = prompt)));
            var audit = Fake<IAuditEventStore>.Create(("AppendAsync", args =>
            {
                Audit.Add((string)args[2]!);
                return null;
            }));
            var extractor = new ConversationMemoryExtractor(resolver, memories, audit,
                NullLogger<ConversationMemoryExtractor>.Instance);

            await extractor.ExtractAndStoreAsync(Owner, Guid.NewGuid(), userMessage, default, context: context);
        }
    }

    private static string Reply(params object[] candidates) => JsonSerializer.Serialize(candidates);

    [Fact]
    public async Task Enrich_replaces_the_memory_with_the_combined_statement_and_keeps_its_importance()
    {
        var tennis = Memory("routine", "Plays tennis.", importance: 0.8f);
        var harness = new Harness();

        await harness.RunAsync(Reply(new
        {
            kind = "routine", content = "Plays tennis on Tuesday evenings with Mark.", importance = 0.4,
            confidence = 0.95, action = "enrich", targetMemoryId = tennis.Id
        }), "I play on Tuesday evenings with Mark.", [tennis]);

        var replaced = Assert.Single(harness.Replaced);
        Assert.Equal(tennis.Id, replaced.Target);
        Assert.Equal(0.8f, replaced.Importance);
        Assert.Empty(harness.Stored);
        Assert.Equal(["memory.enriched"], harness.Audit);
    }

    [Fact]
    public async Task Enriching_a_pinned_memory_adds_a_new_one_instead()
    {
        var pinned = Memory("routine", "Plays tennis.", pinned: true);
        var harness = new Harness();

        await harness.RunAsync(Reply(new
        {
            kind = "routine", content = "Plays tennis on Tuesdays.", importance = 0.6, confidence = 0.95,
            action = "enrich", targetMemoryId = pinned.Id
        }), "I play tennis on Tuesdays.", [pinned]);

        Assert.Empty(harness.Replaced);
        Assert.Single(harness.Stored);
        Assert.Equal(["memory.extracted"], harness.Audit);
    }

    [Fact]
    public async Task Expire_ends_an_unpinned_memory_without_storing_a_new_one()
    {
        var car = Memory("fact", "Owns a red Volvo.");
        var harness = new Harness();

        await harness.RunAsync(Reply(new
        {
            kind = "fact", content = "Sold the Volvo.", importance = 0.5, confidence = 0.95, action = "expire",
            targetMemoryId = car.Id
        }), "I sold my car last week.", [car]);

        Assert.Equal([car.Id], harness.Expired);
        Assert.Empty(harness.Stored);
        Assert.Equal(["memory.expired"], harness.Audit);
    }

    [Theory]
    [InlineData(true, 0.95)]
    [InlineData(false, 0.85)]
    public async Task Expire_needs_an_unpinned_target_and_high_confidence(bool pinned, double confidence)
    {
        var car = Memory("fact", "Owns a red Volvo.", pinned);
        var harness = new Harness();

        await harness.RunAsync(Reply(new
        {
            kind = "fact", content = "Sold the Volvo.", importance = 0.5, confidence, action = "expire",
            targetMemoryId = car.Id
        }), "I think I might sell my car.", [car]);

        Assert.Empty(harness.Expired);
        Assert.Empty(harness.Audit);
    }

    [Fact]
    public async Task Expire_ignores_targets_that_were_not_offered()
    {
        var harness = new Harness();

        await harness.RunAsync(Reply(new
        {
            kind = "fact", content = "Gone.", importance = 0.5, confidence = 0.99, action = "expire",
            targetMemoryId = Guid.NewGuid()
        }), "That is no longer true at all.", [Memory("fact", "Owns a red Volvo.")]);

        Assert.Empty(harness.Expired);
    }

    [Fact]
    public async Task A_short_reply_is_understood_through_the_question_it_answers()
    {
        var gym = Memory("routine", "Goes to the gym on Tuesdays.");
        var harness = new Harness();
        MemoryExtractionTurn[] context =
        [
            new("user", "Plan my week."),
            new("assistant", "Do you still go to the gym on Tuesdays?")
        ];

        await harness.RunAsync(Reply(new
        {
            kind = "routine", content = "Goes to the gym on Thursdays.", importance = 0.6, confidence = 0.9,
            action = "supersede", targetMemoryId = gym.Id
        }), "Thursdays now", [gym], context);

        Assert.Equal("Do you still go to the gym on Tuesdays? Thursdays now", Assert.Single(harness.Searches));
        Assert.Contains("Do you still go to the gym on Tuesdays?", harness.Prompt);
        Assert.Equal(gym.Id, Assert.Single(harness.Replaced).Target);
        Assert.Equal(["memory.superseded"], harness.Audit);
    }

    [Fact]
    public async Task A_short_message_without_context_is_skipped()
    {
        var harness = new Harness();

        await harness.RunAsync(Reply(new
        {
            kind = "routine", content = "Thursdays.", importance = 0.6, confidence = 0.9, action = "add",
            targetMemoryId = (Guid?)null
        }), "Thursdays", [Memory("fact", "Anything.")]);

        Assert.Null(harness.Prompt);
        Assert.Empty(harness.Stored);
    }

    [Fact]
    public void Long_messages_are_searched_on_their_own() =>
        Assert.Equal("I moved to Amsterdam last month with my partner and our two cats",
            ConversationMemoryExtractor.SearchQuery("I moved to Amsterdam last month with my partner and our two cats",
                [new MemoryExtractionTurn("assistant", "Where do you live?")]));

    private sealed class Replying(string reply, Action<string> capture) : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            capture(messages.Last().Text);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
