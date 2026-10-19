using Jarvis.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolHarnessLimitsTests
{
    [Fact]
    public void Small_results_pass_through_untouched()
    {
        var result = new { items = new[] { 1, 2, 3 } };

        Assert.Same(result, ToolFailureFeedback.LimitSize(result, 100));
        Assert.Equal("short", ToolFailureFeedback.LimitSize("short", 100));
        Assert.Null(ToolFailureFeedback.LimitSize(null, 100));
    }

    [Fact]
    public void Large_results_keep_head_and_tail_with_a_marker()
    {
        var text = new string('a', 1_000) + new string('z', 1_000);

        var limited = Assert.IsType<string>(ToolFailureFeedback.LimitSize(text, 200));

        Assert.StartsWith(new string('a', 150), limited);
        Assert.EndsWith(new string('z', 50), limited);
        Assert.Contains("[truncated 1800 characters]", limited);
        Assert.True(limited.Length < 300);
    }

    [Fact]
    public async Task A_tool_result_over_the_cap_is_truncated_before_the_model_sees_it()
    {
        var tool = AIFunctionFactory.Create(new Func<string>(() => new string('x', 5_000)), "Dump");
        var model = new LoopingModel("Dump", calls: 1);
        var agent = CreateAgent(model, tool, maxResultChars: 400);

        await agent.RunAsync("Dump it");

        var seen = Assert.Single(model.ResultsSeen);
        Assert.Contains("[truncated", seen);
        Assert.True(seen.Length < 600);
    }

    [Fact]
    public async Task The_same_call_is_refused_after_two_runs_in_one_turn()
    {
        var runs = 0;
        var tool = AIFunctionFactory.Create(new Func<string>(() => $"run {++runs}"), "Poll");
        var model = new LoopingModel("Poll", calls: 4);
        var agent = CreateAgent(model, tool);

        await agent.RunAsync("Keep polling");

        Assert.Equal(ToolFailureFeedback.MaxIdenticalCalls, runs);
        Assert.Contains("already called Poll", model.ResultsSeen[^1]);
    }

    [Fact]
    public async Task Repeats_from_an_earlier_turn_do_not_count()
    {
        var runs = 0;
        var tool = AIFunctionFactory.Create(new Func<string>(() => $"run {++runs}"), "Poll");
        var agent = CreateAgent(new LoopingModel("Poll", calls: 1), tool);
        var session = await agent.CreateSessionAsync();

        for (var turn = 0; turn < 4; turn++) await agent.RunAsync("Poll once", session);

        Assert.Equal(4, runs);
    }

    [Fact]
    public async Task Different_arguments_are_not_repeats()
    {
        var runs = 0;
        var tool = AIFunctionFactory.Create(new Func<string, string>(id => $"{id} run {++runs}"), "Fetch");
        var model = new LoopingModel("Fetch", calls: 4, vary: true);
        var agent = CreateAgent(model, tool);

        await agent.RunAsync("Fetch four things");

        Assert.Equal(4, runs);
    }

    private static ChatClientAgent CreateAgent(IChatClient model, AITool tool, int maxResultChars = 16_000)
    {
        var agent = new ChatClientAgent(model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = [tool], AllowMultipleToolCalls = false },
            ChatHistoryProvider = JarvisAgentFactory.CreateHistoryProvider()
        });
        ToolFailureFeedback.Configure(agent.ChatClient.GetService<FunctionInvokingChatClient>(), maxResultChars);
        return agent;
    }

    /// <summary>Calls one tool <paramref name="calls"/> times in a turn, then answers "Done.".</summary>
    private sealed class LoopingModel(string toolName, int calls, bool vary = false) : IChatClient
    {
        public List<string> ResultsSeen { get; private set; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            var turn = list.Skip(list.FindLastIndex(m => m.Role == ChatRole.User)).ToList();
            var results = turn.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            ResultsSeen = results.Select(result => result.Result?.ToString() ?? result.Exception?.Message ?? string.Empty).ToList();
            if (results.Length >= calls)
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));

            Dictionary<string, object?> arguments = vary ? new() { ["id"] = $"item-{results.Length}" } : [];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(Guid.NewGuid().ToString("N"), toolName, arguments)])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate { Role = message.Role, Contents = message.Contents };
        }
    }
}
