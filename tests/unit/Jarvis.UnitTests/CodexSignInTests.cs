using Jarvis.Agents;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CodexSignInTests
{
    [Fact]
    public void Reads_the_link_and_code_from_colored_cli_output()
    {
        const string output = "Welcome to Codex [v\u001b[90m0.145.0\u001b[0m]\n" +
            "1. Open this link in your browser and sign in to your account\n" +
            "   \u001b[94mhttps://auth.openai.com/codex/device\u001b[0m\n" +
            "2. Enter this one-time code \u001b[90m(expires in 15 minutes)\u001b[0m\n" +
            "   \u001b[94mAB12-CD34E\u001b[0m\n";
        var now = DateTimeOffset.Parse("2026-10-03T10:00:00Z");

        var code = CodexSignIn.ParseDeviceCode(output, now);

        Assert.NotNull(code);
        Assert.Equal("https://auth.openai.com/codex/device", code.VerificationUrl);
        Assert.Equal("AB12-CD34E", code.UserCode);
        Assert.Equal(now.AddMinutes(15), code.ExpiresAt);
    }

    [Fact]
    public void Waits_until_both_the_link_and_the_code_have_arrived() =>
        Assert.Null(CodexSignIn.ParseDeviceCode("1. Open this link\n   https://auth.openai.com/codex/device\n",
            DateTimeOffset.UtcNow));
}
