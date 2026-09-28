using Jarvis.Application.Integrations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpStdioCommandValidatorTests
{
    [Fact]
    public void Normalize_accepts_npx_with_y_and_package()
    {
        var (command, args) = McpStdioCommandValidator.Normalize("npx", ["-y", "@scope/server-name"]);
        Assert.Equal("npx", command);
        Assert.Equal(["-y", "@scope/server-name"], args);
    }

    [Theory]
    [InlineData("bash", "-c", "rm")]
    [InlineData("npx", "package")]
    public void Normalize_rejects_unsafe_commands(params string[] parts)
    {
        Assert.Throws<ArgumentException>(() =>
            McpStdioCommandValidator.Normalize(parts[0], parts.Skip(1).ToArray()));
    }
}
