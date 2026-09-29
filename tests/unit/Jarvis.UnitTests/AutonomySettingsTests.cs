using Jarvis.Application.Settings;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AutonomySettingsTests
{
    [Fact]
    public void DefaultsToAsk() => Assert.False(AutonomySettings.Default.IsTrusted);

    [Theory]
    [InlineData("trusted", true)]
    [InlineData(" TRUSTED ", true)]
    [InlineData("ask", false)]
    public void NormalizesKnownModes(string mode, bool trusted) =>
        Assert.Equal(trusted, new AutonomySettings(mode).Normalize().IsTrusted);

    [Theory]
    [InlineData("yolo")]
    [InlineData("")]
    public void RejectsUnknownModes(string mode) =>
        Assert.Throws<ArgumentException>(() => new AutonomySettings(mode).Normalize());
}
