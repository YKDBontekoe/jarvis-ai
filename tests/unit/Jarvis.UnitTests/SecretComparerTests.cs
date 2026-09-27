using Jarvis.Application.Security;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SecretComparerTests
{
    [Fact]
    public void Equal_secrets_match() =>
        Assert.True(SecretComparer.FixedTimeEquals("voice-secret", "voice-secret"));

    [Fact]
    public void Different_equal_length_secrets_do_not_match() =>
        Assert.False(SecretComparer.FixedTimeEquals("voice-secret", "voice-secreT"));

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("secret", null)]
    [InlineData("", "secret")]
    [InlineData("secret", "")]
    [InlineData("   ", "secret")]
    [InlineData("short", "longer-secret")]
    [InlineData("longer-secret", "short")]
    public void Missing_or_different_length_secrets_do_not_throw(string? expected, string? supplied) =>
        Assert.False(SecretComparer.FixedTimeEquals(expected, supplied));
}
