using Jarvis.Application.Settings;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class VoiceSelectionTests
{
    [Fact]
    public void Empty_catalog_keeps_a_saved_voice_and_otherwise_defers_to_the_cli()
    {
        Assert.Equal("spruce", VoiceSelection.Resolve(" Spruce ", [], null));
        Assert.Null(VoiceSelection.Resolve(null, [], null));
        Assert.Null(VoiceSelection.Resolve("   ", [], "cove"));
    }

    [Fact]
    public void A_saved_voice_must_be_one_the_cli_listed()
    {
        var voices = new[] { "juniper", "cove", "spruce" };
        Assert.Equal("spruce", VoiceSelection.Resolve("Spruce", voices, "cove"));
        Assert.Equal("cove", VoiceSelection.Resolve("marin", voices, "cove"));
        Assert.Equal("cove", VoiceSelection.Resolve(null, voices, "cove"));
        Assert.Equal("juniper", VoiceSelection.Resolve("marin", voices, null));
        Assert.Equal("juniper", VoiceSelection.Resolve("marin", voices, "not-listed"));
    }
}
