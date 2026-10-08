using Jarvis.Api.Channels;
using Jarvis.Api.Endpoints;
using Jarvis.Application.WhatsApp;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WhatsAppRepliesAndReactionsTests
{
    private static WhatsAppChatMessage Message(string externalId, bool fromMe, string? senderId = null) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "120363025-1@g.us", externalId, fromMe, "Sanne",
            "Eten we vrijdag bij oma?", DateTimeOffset.UnixEpoch, SenderId: senderId);

    [Theory]
    [InlineData("👍", true)]
    [InlineData("❤️", true)]
    [InlineData("👍🏽", true)]
    [InlineData("", true)]
    [InlineData("ok", false)]
    [InlineData("👍👍", false)]
    [InlineData("1", false)]
    [InlineData("👍 ", false)]
    public void Reactions_are_a_single_emoji_or_empty(string emoji, bool valid) =>
        Assert.Equal(valid, WhatsAppReactions.IsValid(emoji));

    [Fact]
    public void A_saved_message_maps_to_its_WhatsApp_key()
    {
        var theirs = BridgeWhatsAppSender.Reference(Message("wa:3EB0ABCD", false, "+31611111111"));
        Assert.Equal(new BridgeMessageRef("3EB0ABCD", false, "+31611111111"), theirs);
        // Your own messages need no participant.
        var mine = BridgeWhatsAppSender.Reference(Message("wa:3EB0ABCE", true, "+31611111111"));
        Assert.Equal(new BridgeMessageRef("3EB0ABCE", true, null), mine);
        // Messages that did not come through the bridge cannot be quoted or reacted to.
        Assert.Null(BridgeWhatsAppSender.Reference(Message("import:1", false)));
    }

    [Fact]
    public void Voice_note_waveforms_are_kept_clean_and_only_for_audio()
    {
        var samples = Enumerable.Range(0, 80).Select(i => i * 2 - 10).ToArray();
        var stored = WhatsAppMediaCodec.Prepare(
            new WhatsAppIncomingMedia("audio", "audio/ogg", Seconds: 4, Voice: true, Waveform: samples), null, null);
        var (media, _) = WhatsAppMediaCodec.Read(stored!.Json);
        Assert.NotNull(media!.Waveform);
        Assert.Equal(64, media.Waveform!.Count);
        Assert.Equal(0, media.Waveform[0]);
        Assert.All(media.Waveform, sample => Assert.InRange(sample, 0, 100));

        var silent = WhatsAppMediaCodec.Prepare(
            new WhatsAppIncomingMedia("audio", Waveform: new int[10]), null, null);
        Assert.Null(WhatsAppMediaCodec.Read(silent!.Json).Media!.Waveform);

        var photo = WhatsAppMediaCodec.Prepare(
            new WhatsAppIncomingMedia("image", "image/jpeg", Waveform: [10, 20]), null, null);
        Assert.Null(WhatsAppMediaCodec.Read(photo!.Json).Media!.Waveform);
    }
}
