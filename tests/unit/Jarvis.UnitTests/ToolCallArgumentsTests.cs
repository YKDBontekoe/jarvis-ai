using Jarvis.Application.Conversations;
using System.Text.Json;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolCallArgumentsTests
{
    [Fact]
    public void Parses_primitives_nested_objects_and_arrays()
    {
        var arguments = ToolCallArguments.Parse(
            """{"title":"Buy milk","count":2,"enabled":true,"tags":["a",1],"nested":{"id":"m1"},"empty":null}""");

        Assert.Equal("Buy milk", arguments["title"]);
        Assert.Equal(2L, arguments["count"]);
        Assert.Equal(true, arguments["enabled"]);
        Assert.Null(arguments["empty"]);
        var tags = Assert.IsType<object?[]>(arguments["tags"]);
        Assert.Equal(["a", 1L], tags);
        var nested = Assert.IsType<Dictionary<string, object?>>(arguments["nested"]);
        Assert.Equal("m1", nested["id"]);
    }

    [Fact]
    public void Empty_or_missing_json_is_an_empty_object()
    {
        Assert.Empty(ToolCallArguments.Parse(null));
        Assert.Empty(ToolCallArguments.Parse(""));
        Assert.Empty(ToolCallArguments.Parse("   "));
        Assert.Empty(ToolCallArguments.Parse("{}"));
    }

    [Fact]
    public void Rejects_non_object_payloads()
    {
        Assert.ThrowsAny<JsonException>(() => ToolCallArguments.Parse("[]"));
        Assert.ThrowsAny<JsonException>(() => ToolCallArguments.Parse("\"x\""));
        Assert.ThrowsAny<JsonException>(() => ToolCallArguments.Parse("{"));
    }
}
