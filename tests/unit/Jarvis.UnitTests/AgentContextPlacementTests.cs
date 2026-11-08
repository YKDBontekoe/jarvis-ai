using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Pins down how Agent Framework places and stores context-provider messages, because tool-group selection and
/// session size both depend on it.
/// </summary>
public sealed class AgentContextPlacementTests
{
    private const string ContextMarker = "CONTEXT-BLOCK-MARKER";

    [Fact]
    public async Task Context_blocks_reach_the_model_but_are_not_stored_in_the_session()
    {
        var client = new RecordingChatClient();
        var agent = CreateAgent(client);
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync("first question", session);
        await agent.RunAsync("second question", session);

        Assert.All(client.Requests, request => Assert.Contains(request, m => m.Text.Contains(ContextMarker)));
        var stored = (await agent.SerializeSessionAsync(session)).GetRawText();
        Assert.DoesNotContain(ContextMarker, stored);
        Assert.Contains("first question", stored);
    }

    [Fact]
    public async Task Tool_groups_follow_the_users_words_not_the_context_block()
    {
        var client = new RecordingChatClient();
        var agent = CreateAgent(client, contextText: $"{ContextMarker} budget expense inbox goal");
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync("Remind me to call the dentist", session);

        var offered = client.OfferedTools.Single();
        Assert.Contains("CreateReminder", offered);
        Assert.DoesNotContain("LogExpense", offered);
    }

    [Fact]
    public void The_prompt_prefix_through_the_tools_does_not_change_as_the_conversation_grows()
    {
        var options = new ChatOptions { Instructions = "Be brief." };
        AIFunction[] tools = [AIFunctionFactory.Create(() => "ok", "CreateReminder")];
        var first = CodexCliChatClient.BuildPrompt([new ChatMessage(ChatRole.User, "hello")], options, tools, false).Text;
        var next = CodexCliChatClient.BuildPrompt(
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "Hi."),
            new ChatMessage(ChatRole.User, "remind me")
        ], options, tools, false).Text;

        // Every tool step and every turn appends to the conversation; everything before it must stay a cacheable prefix.
        var conversationStart = first.IndexOf("\nConversation:", StringComparison.Ordinal);
        Assert.True(first.IndexOf("Available Jarvis functions", StringComparison.Ordinal) < conversationStart);
        Assert.Equal(first[..conversationStart], next[..conversationStart]);
        Assert.EndsWith("Return only the JSON object required by the output schema.", first.TrimEnd());
    }

    [Fact]
    public void Loading_a_tool_group_only_changes_the_end_of_the_tool_list()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };
        AIFunction Tool(string name) => AIFunctionFactory.Create(() => "ok", name);

        // A grouped tool registered before an always-on one still goes after it.
        var before = CodexCliChatClient.BuildPrompt(messages, null, [Tool("CreateReminder"), Tool("Remember")], false).Text;
        var after = CodexCliChatClient.BuildPrompt(messages, null,
            [Tool("CreateReminder"), Tool("LogExpense"), Tool("Remember")], false).Text;

        var alwaysOnEnd = before.IndexOf("\nConversation:", StringComparison.Ordinal);
        Assert.Equal(before[..alwaysOnEnd], after[..alwaysOnEnd]);
        Assert.True(after.IndexOf("- LogExpense:", StringComparison.Ordinal) >
                    after.IndexOf("- Remember:", StringComparison.Ordinal));
    }

    private static AIAgent CreateAgent(RecordingChatClient client, string contextText = ContextMarker)
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(() => "ok", "CreateReminder"),
            AIFunctionFactory.Create(() => "ok", "LogExpense"),
            ToolGroups.CreateLoadTools(new HashSet<string> { "CreateReminder", "LogExpense" })
        ];
        return new ChatClientAgent(new ToolGroupChatClient(client), new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = tools },
            ChatHistoryProvider = JarvisAgentFactory.CreateHistoryProvider(),
            AIContextProviders = [new StaticContextProvider(contextText)]
        });
    }

    private sealed class StaticContextProvider(string text) : MessageAIContextProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
            InvokingContext context, CancellationToken cancellationToken = default) =>
            new([new ChatMessage(ChatRole.User, text)]);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
        public List<IReadOnlyList<string>> OfferedTools { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            OfferedTools.Add(options?.Tools?.Select(tool => tool.Name).ToList() ?? []);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            OfferedTools.Add(options?.Tools?.Select(tool => tool.Name).ToList() ?? []);
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
