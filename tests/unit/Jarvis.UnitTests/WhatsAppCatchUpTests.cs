using Jarvis.Agents.WhatsApp;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WhatsAppCatchUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseCatchUp_reads_summary_and_reply_items()
    {
        var result = WhatsAppAssistant.ParseCatchUp("""
            ```json
            {"summary":"  Piet wil   zaterdag voetballen. ","toReply":[{"who":"Piet","about":"Kom je zaterdag?"},{"who":"Anna","about":""}],"extra":1}
            ```
            """, 3, Now);

        Assert.NotNull(result);
        Assert.Equal("Piet wil zaterdag voetballen.", result.Summary);
        var item = Assert.Single(result.ToReply);
        Assert.Equal("Piet", item.Who);
        Assert.Equal(3, result.MessageCount);
        Assert.Equal(Now, result.GeneratedAt);
    }

    [Fact]
    public void ParseCatchUp_caps_lengths_and_item_count()
    {
        var items = string.Join(',', Enumerable.Range(0, 8).Select(i => $$"""{"who":"P{{i}}","about":"{{new string('x', 300)}}"}"""));
        var result = WhatsAppAssistant.ParseCatchUp(
            $$"""{"summary":"{{new string('s', 900)}}","toReply":[{{items}}]}""", 2, Now);

        Assert.NotNull(result);
        Assert.Equal(WhatsAppAssistant.MaxCatchUpSummaryLength, result.Summary.Length);
        Assert.Equal(5, result.ToReply.Count);
        Assert.All(result.ToReply, x => Assert.Equal(200, x.About.Length));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{\"summary\":\"   \"}")]
    [InlineData("{\"summary\": 5}")]
    [InlineData("{broken")]
    public void ParseCatchUp_returns_null_without_a_usable_summary(string? text) =>
        Assert.Null(WhatsAppAssistant.ParseCatchUp(text, 2, Now));
}
