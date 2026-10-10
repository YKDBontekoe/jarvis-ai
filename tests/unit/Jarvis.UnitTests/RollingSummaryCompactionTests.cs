using Jarvis.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class RollingSummaryCompactionTests
{
    /// <summary>One user turn of roughly <paramref name="tokens"/> tokens with a tool call and a reply.</summary>
    private static IEnumerable<ChatMessage> Turn(int number, int tokens = 8_000)
    {
        var callId = $"call-{number}";
        yield return new ChatMessage(ChatRole.User, $"Question {number}: " + new string('q', tokens * 2));
        yield return new ChatMessage(ChatRole.User, $"Context for turn {number}");
        yield return new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, "SearchMemory")]);
        yield return new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, "result " + new string('r', tokens))]);
        yield return new ChatMessage(ChatRole.Assistant, $"Answer {number}: " + new string('a', tokens));
    }

    private static List<ChatMessage> Conversation(int turns) =>
        Enumerable.Range(1, turns).SelectMany(number => Turn(number)).ToList();

    private sealed class CountingSummarizer : IChatClient
    {
        public List<string> Requests { get; } = [];
        public bool Fail { get; set; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages.Last().Text;
            Requests.Add(request);
            if (Fail) throw new InvalidOperationException("model down");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"summary #{Requests.Count}")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static (RollingSummaryCompaction Compaction, CountingSummarizer Model, RollingSummaryCache Cache) Create()
    {
        var model = new CountingSummarizer();
        var cache = new RollingSummaryCache();
        return (new RollingSummaryCompaction(_ => Task.FromResult<IChatClient>(model), cache, NullLogger.Instance),
            model, cache);
    }

    private static async Task WaitForSummaryAsync(RollingSummaryCache cache, IList<ChatMessage> messages)
    {
        var plan = RollingSummaryCompaction.Plan(messages)!;
        var key = RollingSummaryCompaction.Key(messages, plan.Start, plan.CutFor(plan.Level));
        for (var attempt = 0; attempt < 200 && !cache.TryGet(key, out _); attempt++) await Task.Delay(10);
    }

    [Fact]
    public void A_conversation_under_the_trigger_is_left_alone()
    {
        var (compaction, model, _) = Create();
        var messages = Conversation(5);

        Assert.Null(RollingSummaryCompaction.Plan(messages));
        Assert.Same(messages, compaction.Compact(messages));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task The_first_long_turn_is_not_delayed_and_the_next_one_uses_the_summary()
    {
        var (compaction, model, cache) = Create();
        var messages = Conversation(12);

        Assert.Same(messages, compaction.Compact(messages));
        await WaitForSummaryAsync(cache, messages);
        var compacted = compaction.Compact(messages);

        Assert.Single(model.Requests);
        Assert.StartsWith(RollingSummaryCompaction.SummaryPrefix, compacted[0].Text);
        Assert.Contains("summary #1", compacted[0].Text);
        Assert.StartsWith("Question", compacted[1].Text);
        Assert.True(compacted.Sum(RollingSummaryCompaction.EstimateTokens) <= RollingSummaryCompaction.TriggerTokens);
        Assert.True(compacted.Skip(1).Sum(RollingSummaryCompaction.EstimateTokens) >=
                    RollingSummaryCompaction.KeepRecentTokens);
    }

    [Fact]
    public async Task A_new_small_turn_reuses_the_cached_summary_without_a_model_call()
    {
        var (compaction, model, cache) = Create();
        var messages = Conversation(12);
        compaction.Compact(messages);
        await WaitForSummaryAsync(cache, messages);

        messages.Add(new ChatMessage(ChatRole.User, "One more short question"));
        var compacted = compaction.Compact(messages);

        Assert.Single(model.Requests);
        Assert.Contains("summary #1", compacted[0].Text);
        Assert.Equal("One more short question", compacted[^1].Text);
    }

    [Fact]
    public async Task A_new_block_extends_the_previous_summary_and_keeps_using_it_meanwhile()
    {
        var (compaction, model, cache) = Create();
        var messages = Conversation(12);
        compaction.Compact(messages);
        await WaitForSummaryAsync(cache, messages);

        messages.AddRange(Enumerable.Range(13, 4).SelectMany(number => Turn(number)));
        var meanwhile = compaction.Compact(messages);
        await WaitForSummaryAsync(cache, messages);
        var after = compaction.Compact(messages);

        Assert.Contains("summary #1", meanwhile[0].Text);
        Assert.Equal(2, model.Requests.Count);
        Assert.StartsWith("previous_summary:\nsummary #1", model.Requests[1]);
        Assert.DoesNotContain("Question 1:", model.Requests[1]);
        Assert.Contains("summary #2", after[0].Text);
    }

    [Fact]
    public void Cuts_only_fall_where_a_user_turn_starts()
    {
        var messages = Conversation(14);
        var plan = RollingSummaryCompaction.Plan(messages)!;

        for (var level = 1; level <= plan.Level; level++)
        {
            var cut = plan.CutFor(level);
            Assert.StartsWith("Question", messages[cut].Text);
        }
    }

    [Fact]
    public async Task A_failing_model_leaves_the_conversation_to_the_truncation_backstop()
    {
        var (compaction, model, cache) = Create();
        model.Fail = true;
        var messages = Conversation(12);

        compaction.Compact(messages);
        for (var attempt = 0; attempt < 100 && model.Requests.Count == 0; attempt++) await Task.Delay(10);
        await Task.Delay(50);

        Assert.Same(messages, compaction.Compact(messages));
        var plan = RollingSummaryCompaction.Plan(messages)!;
        Assert.False(cache.TryGet(RollingSummaryCompaction.Key(messages, plan.Start, plan.CutFor(plan.Level)), out _));
    }

    [Fact]
    public void The_transcript_clips_tool_results_and_keeps_the_most_recent_part()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Plan the trip"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "BrowseTheWeb")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", new string('x', 5_000))])
        };

        var transcript = RollingSummaryCompaction.Transcript("old", messages, 0, messages.Count);

        Assert.StartsWith("previous_summary:\nold", transcript);
        Assert.Contains("[tool call] BrowseTheWeb", transcript);
        Assert.True(transcript.Length < 1_200);
    }
}
