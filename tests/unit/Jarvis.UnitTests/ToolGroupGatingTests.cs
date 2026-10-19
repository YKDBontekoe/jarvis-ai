using Jarvis.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolGroupGatingTests
{
    private const string Guide = "MONEY-GUIDANCE-MARKER";

    [Fact]
    public async Task Guidance_is_left_out_of_a_turn_that_does_not_use_its_group()
    {
        var model = new ScriptedModel();
        var agent = CreateAgent(model);

        await agent.RunAsync("Remind me to call the dentist", await agent.CreateSessionAsync());

        Assert.DoesNotContain(model.LastRequestText, text => text.Contains(Guide));
    }

    [Fact]
    public async Task Guidance_is_sent_when_the_users_words_pick_the_group()
    {
        var model = new ScriptedModel();
        var agent = CreateAgent(model);

        await agent.RunAsync("I spent €12 on lunch", await agent.CreateSessionAsync());

        Assert.Contains(model.LastRequestText, text => text.Contains(Guide));
    }

    [Fact]
    public async Task Guidance_stays_for_a_follow_up_after_a_tool_of_the_group_ran()
    {
        var model = new ScriptedModel(callOnFirstTurn: "LogExpense");
        var agent = CreateAgent(model);
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync("I spent €12 on lunch", session);
        await agent.RunAsync("thanks, and make it 14", session);

        Assert.Contains(model.LastRequestText, text => text.Contains(Guide));
    }

    [Fact]
    public async Task Guidance_drops_out_again_once_the_group_is_no_longer_in_use()
    {
        var model = new ScriptedModel(callOnFirstTurn: "LogExpense");
        var agent = CreateAgent(model);
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync("I spent €12 on lunch", session);
        foreach (var message in new[] { "hello", "how are you", "what is up" })
            await agent.RunAsync(message, session);

        Assert.DoesNotContain(model.LastRequestText, text => text.Contains(Guide));
    }

    private static AIAgent CreateAgent(ScriptedModel model)
    {
        var history = JarvisAgentFactory.CreateHistoryProvider();
        List<AITool> tools =
        [
            AIFunctionFactory.Create(() => "ok", "CreateReminder"),
            AIFunctionFactory.Create(() => "logged", "LogExpense"),
            ToolGroups.CreateLoadTools(new HashSet<string> { "CreateReminder", "LogExpense" })
        ];
        var agent = new ChatClientAgent(new ToolGroupChatClient(model), new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = tools, AllowMultipleToolCalls = false },
            ChatHistoryProvider = history,
            AIContextProviders = [new ToolGroupGatedContextProvider(new StaticProvider(Guide), "money", history)]
        });
        ToolFailureFeedback.Configure(agent.ChatClient.GetService<FunctionInvokingChatClient>());
        return agent;
    }

    private sealed class StaticProvider(string text) : MessageAIContextProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
            InvokingContext context, CancellationToken cancellationToken = default) =>
            new([new ChatMessage(ChatRole.User, text)]);
    }

    /// <summary>Optionally calls a tool once on the first user turn, then answers "Done.".</summary>
    private sealed class ScriptedModel(string? callOnFirstTurn = null) : IChatClient
    {
        private bool called;
        public List<string> LastRequestText { get; private set; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            LastRequestText = list.Select(message => message.Text).ToList();
            if (callOnFirstTurn is not null && !called)
            {
                called = true;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", callOnFirstTurn, new Dictionary<string, object?>())])));
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
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
