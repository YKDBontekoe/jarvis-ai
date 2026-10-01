using System.Text.Json;
using Jarvis.Application.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class RegenerateTruncationTests
{
    [Fact]
    public async Task Drops_the_last_turn_and_keeps_earlier_ones()
    {
        var agent = new ChatClientAgent(new EchoClient());
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("First question", session);
        await agent.RunAsync("Second question", session);
        var serialized = (await agent.SerializeSessionAsync(session)).GetRawText();

        var result = AgentSessionJson.TryDropLastTurnForRegenerate(serialized, out var truncated);

        Assert.Equal(RegenerateTruncation.Dropped, result);
        Assert.Contains("First question", truncated);
        Assert.Contains("Echo: First question", truncated);
        Assert.DoesNotContain("Second question", truncated);
        using var document = JsonDocument.Parse(truncated);
        var restored = await agent.DeserializeSessionAsync(document.RootElement);
        var again = await agent.RunAsync("Second question", restored);
        Assert.Equal("Echo: Second question", again.Text);
    }

    [Fact]
    public void Refuses_a_turn_that_called_tools()
    {
        const string json = """
            {"messages":[
              {"role":"user","contents":[{"$type":"text","text":"Remind me at five"}]},
              {"role":"assistant","contents":[{"$type":"functionCall","callId":"c1","name":"CreateReminder"}]},
              {"role":"tool","contents":[{"$type":"functionResult","callId":"c1","result":"ok"}]},
              {"role":"assistant","contents":[{"$type":"text","text":"Done."}]}
            ]}
            """;

        Assert.Equal(RegenerateTruncation.UsedTools,
            AgentSessionJson.TryDropLastTurnForRegenerate(json, out var unchanged));
        Assert.Equal(json, unchanged);
    }

    [Fact]
    public void Reports_when_there_is_no_user_turn()
    {
        Assert.Equal(RegenerateTruncation.NoTurn,
            AgentSessionJson.TryDropLastTurnForRegenerate("""{"messages":[]}""", out _));
        Assert.Equal(RegenerateTruncation.NoTurn,
            AgentSessionJson.TryDropLastTurnForRegenerate("not json", out _));
    }

    private sealed class EchoClient : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "Echo: " + messages.Last(message => message.Role == ChatRole.User).Text)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
