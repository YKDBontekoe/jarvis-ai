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
    public void The_prompt_prefix_does_not_change_when_the_offered_tools_change()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };
        var options = new ChatOptions { Instructions = "Be brief." };
        AIFunction Tool(string name) => AIFunctionFactory.Create(() => "ok", name);

        var before = CodexCliChatClient.BuildPrompt(messages, options, [Tool("CreateReminder")], false).Text;
        var after = CodexCliChatClient.BuildPrompt(messages, options, [Tool("CreateReminder"), Tool("LogExpense")], false).Text;

        var conversationEnd = before.IndexOf("\nAvailable Jarvis functions", StringComparison.Ordinal);
        Assert.True(conversationEnd > before.IndexOf("[user]", StringComparison.Ordinal));
        Assert.Equal(before[..conversationEnd], after[..conversationEnd]);
        Assert.EndsWith("Return only the JSON object required by the output schema.", before.TrimEnd());
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
