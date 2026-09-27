using Jarvis.Agents;
using Jarvis.Application.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class MemoryExtractionTests
{
    [Fact]
    public void Unwraps_fenced_and_padded_json_arrays()
    {
        const string payload = """[{"kind":"fact","content":"Prefers dark mode"}]""";
        Assert.Equal(payload, MemoryExtractionJson.UnwrapArray($"""
            ```json
            {payload}
            ```
            """));
        Assert.Equal(payload, MemoryExtractionJson.UnwrapArray($"Sure.\n{payload}\nThanks."));
        Assert.Null(MemoryExtractionJson.UnwrapArray("no memories"));
    }

    [Fact]
    public void ForUpdate_clears_already_expired_validity()
    {
        var expired = DateTimeOffset.UtcNow.AddMinutes(-5);
        var future = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Null(MemoryValidity.ForUpdate(expired));
        Assert.Equal(future, MemoryValidity.ForUpdate(future));
        Assert.Null(MemoryValidity.ForUpdate(null));
    }
}
