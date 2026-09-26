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
}
