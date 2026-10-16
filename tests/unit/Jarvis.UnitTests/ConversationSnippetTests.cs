using Jarvis.Application.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConversationSnippetTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   \n  ", null)]
    [InlineData("**Done.** I added it to your *calendar*.", "Done. I added it to your calendar.")]
    [InlineData("# Plan\n- Pack the bag\n- Leave at 16:00", "Plan Pack the bag Leave at 16:00")]
    [InlineData("See [the route](https://maps.example/x) here", "See the route here")]
    [InlineData("> quoted\n\nreply", "quoted reply")]
    [InlineData("Run this:\n```bash\nrm -rf /tmp/x\n```\nthen tell me", "Run this: then tell me")]
    [InlineData("```json\n{\"a\":1}\n```", null)]
    [InlineData("Use `dotnet test` now", "Use dotnet test now")]
    public void Build_turns_a_message_into_one_plain_line(string? content, string? expected) =>
        Assert.Equal(expected, ConversationSnippets.Build(content));

    [Fact]
    public void Build_cuts_long_text_on_a_word_with_an_ellipsis()
    {
        var text = string.Join(' ', Enumerable.Repeat("woord", 60));

        var snippet = ConversationSnippets.Build(text)!;

        Assert.True(snippet.Length <= ConversationSnippets.MaxLength);
        Assert.EndsWith("woord…", snippet);
    }
}
