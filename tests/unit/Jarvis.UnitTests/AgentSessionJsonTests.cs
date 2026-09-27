using Jarvis.Application.Conversations;
using System.Text.Json;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AgentSessionJsonTests
{
    [Fact]
    public void RestoresNestedMetadataWithoutChangingValues()
    {
        const string stored = """{"messages":[{"contents":[{"text":"Hello","$type":"text"},{"callId":"call-1","$type":"functionCall","arguments":{"title":"$type is data","count":2}}]}],"empty":null}""";
        var prepared = AgentSessionJson.PrepareForRead(stored);
        using var document = JsonDocument.Parse(prepared);
        var contents = document.RootElement.GetProperty("messages")[0].GetProperty("contents");
        Assert.Equal("$type", contents[0].EnumerateObject().First().Name);
        Assert.Equal("$type", contents[1].EnumerateObject().First().Name);
        Assert.Equal("Hello", contents[0].GetProperty("text").GetString());
        Assert.Equal("$type is data", contents[1].GetProperty("arguments").GetProperty("title").GetString());
        Assert.Equal(2, contents[1].GetProperty("arguments").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("empty").ValueKind);
    }

    [Fact]
    public void RestoresTypeMetadataBeforeIdAfterAlphabeticalStorage()
    {
        const string stored = """{"contents":[{"$id":"1","arguments":{"title":"Keep"},"callId":"call-1","$type":"functionCall"}]}""";
        var prepared = AgentSessionJson.PrepareForRead(stored);
        using var document = JsonDocument.Parse(prepared);
        var names = document.RootElement.GetProperty("contents")[0].EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal("$type", names[0]);
        Assert.Equal("$id", names[1]);
        Assert.Equal("functionCall", document.RootElement.GetProperty("contents")[0].GetProperty("$type").GetString());
        Assert.Equal("1", document.RootElement.GetProperty("contents")[0].GetProperty("$id").GetString());
    }

    [Fact]
    public void RecoversPlainAssistantTextAndRejectsToolTurns()
    {
        Assert.True(AgentSessionJson.TryGetCompletedAssistantText(
            """{"stateBag":{"messages":[{"contents":[{"text":"Hello","$type":"text"}]},{"contents":[{"text":"Done.","$type":"text"}]}]}}""",
            out var text));
        Assert.Equal("Done.", text);
        Assert.False(AgentSessionJson.TryGetCompletedAssistantText(
            """{"stateBag":{"messages":[{"contents":[{"text":"Remind me tomorrow","$type":"text"}]}]}}""",
            out _));
        Assert.False(AgentSessionJson.TryGetCompletedAssistantText(
            """{"stateBag":{"messages":[{"contents":[{"callId":"c1","$type":"functionCall","name":"CreateReminder"}]}]}}""",
            out _));
        Assert.False(AgentSessionJson.TryGetCompletedAssistantText(
            """{"messages":[{"contents":[{"text":"User","$type":"text"}]},{"role":"user","contents":[{"text":"Still the user","$type":"text"}]}]}""",
            out _));
    }

    [Fact]
    public void RecoversAssistantTextOnlyForTheMatchingUserTurn()
    {
        const string session =
            """{"stateBag":{"messages":[{"contents":[{"text":"Hello","$type":"text"}]},{"contents":[{"text":"Done.","$type":"text"}]}]}}""";
        Assert.True(AgentSessionJson.TryGetCompletedAssistantTextAfterUser(session, "Hello", out var text));
        Assert.Equal("Done.", text);
        Assert.False(AgentSessionJson.TryGetCompletedAssistantTextAfterUser(session, "Something else", out _));
    }

    [Fact]
    public void RecoversPendingApprovalsAndPrefaceFromTheLastAssistantTurn()
    {
        const string session = """
            {"stateBag":{"messages":[
              {"role":"user","contents":[{"text":"Remind me","$type":"text"}]},
              {"role":"assistant","contents":[
                {"text":"I can create that reminder.","$type":"text"},
                {"id":"req-1","$type":"functionApprovalRequest","functionCall":{"callId":"c1","name":"CreateReminder","arguments":{"title":"Dentist"}}}
              ]}
            ]}}
            """;
        Assert.True(AgentSessionJson.TryGetPendingApprovals(session, out var approvals, out var preface));
        Assert.Equal("I can create that reminder.", preface);
        var approval = Assert.Single(approvals);
        Assert.Equal("req-1", approval.RequestId);
        Assert.Equal("c1", approval.ToolCallId);
        Assert.Equal("CreateReminder", approval.ToolName);
        Assert.Contains("Dentist", approval.ArgumentsJson);
        Assert.False(AgentSessionJson.TryGetPendingApprovals(
            """{"stateBag":{"messages":[{"role":"user","contents":[{"text":"Hello","$type":"text"}]}]}}""",
            out _, out _));
    }

    [Fact]
    public void DetectsInFlightToolProgressWithoutRequiringAFinalAssistantTurn()
    {
        const string inFlight = """
            {"stateBag":{"messages":[
              {"role":"user","contents":[{"text":"Remind me","$type":"text"}]},
              {"role":"assistant","contents":[
                {"callId":"c1","$type":"functionCall","name":"CreateReminder","arguments":{"title":"Dentist"}},
                {"callId":"c1","$type":"functionResult","text":"created"}
              ]}
            ]}}
            """;
        Assert.True(AgentSessionJson.HasInFlightProgressAfterUser(inFlight, "Remind me"));
        Assert.False(AgentSessionJson.HasInFlightProgressAfterUser(inFlight, "Something else"));
        Assert.False(AgentSessionJson.HasInFlightProgressAfterUser(
            """{"stateBag":{"messages":[{"contents":[{"text":"Hello","$type":"text"}]},{"contents":[{"text":"Done.","$type":"text"}]}]}}""",
            "Hello"));
        Assert.True(AgentSessionJson.HasInFlightProgressAfterUser(
            """{"stateBag":{"messages":[{"role":"user","contents":[{"text":"Remind me","$type":"text"}]}]}}""",
            "Remind me"));
    }
}
