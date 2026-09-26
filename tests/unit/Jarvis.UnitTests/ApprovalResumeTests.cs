using Jarvis.Agents;
using Jarvis.Application.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Jarvis.UnitTests;

#pragma warning disable MAAI001
public sealed class ApprovalResumeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repeated_reference_context_preserves_approval_decision_after_session_reload(bool approved)
    {
        var executions = 0;
        using var client = new ApprovalFixtureClient();
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() =>
        {
            executions++;
            return "Fixture action executed";
        }, "fixture_action"));
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = [tool], AllowMultipleToolCalls = false },
            AIContextProviders = [new RepeatedReferenceProvider(),
                new CurrentContextCompactionProvider(new TruncationCompactionStrategy(
                    CompactionTriggers.TokensExceed(80_000), minimumPreservedGroups: 4,
                    target: CompactionTriggers.TokensBelow(64_000)),
                    NullLogger<CurrentContextCompactionProvider>.Instance)]
        });
        var session = await agent.CreateSessionAsync();
        ToolApprovalRequestContent? request = null;
        await foreach (var update in agent.RunStreamingAsync("Run the fixture action", session))
            request ??= update.Contents.OfType<ToolApprovalRequestContent>().FirstOrDefault();
        Assert.NotNull(request);
        Assert.Equal(0, executions);

        var serialized = await agent.SerializeSessionAsync(session);
        using var saved = JsonDocument.Parse(AgentSessionJson.PrepareForRead(serialized.GetRawText()));
        session = await agent.DeserializeSessionAsync(saved.RootElement);
        var response = await agent.RunAsync(new ChatMessage(ChatRole.User,
            [request.CreateResponse(approved, approved ? null : "Rejected by fixture owner")]), session);

        Assert.Equal(approved ? 1 : 0, executions);
        Assert.True(client.SawToolResult);
        Assert.Equal("Fixture decision completed", response.Text);
        Assert.DoesNotContain(response.Messages.SelectMany(x => x.Contents),
            content => content is ToolApprovalRequestContent);
    }

    private sealed class RepeatedReferenceProvider : MessageAIContextProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<ChatMessage>>([new(ChatRole.User, "An unrelated task is waiting for approval.")]);
    }

    private sealed class ApprovalFixtureClient : IChatClient
    {
        public bool SawToolResult { get; private set; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(Reply(messages)));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var reply = Reply(messages);
            yield return new ChatResponseUpdate { Role = reply.Role, Contents = reply.Contents };
        }
        private ChatMessage Reply(IEnumerable<ChatMessage> messages)
        {
            SawToolResult |= messages.SelectMany(x => x.Contents).OfType<FunctionResultContent>().Any();
            return SawToolResult
                ? new ChatMessage(ChatRole.Assistant, "Fixture decision completed")
                : new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), "fixture_action")]);
        }
    }
}
#pragma warning restore MAAI001
