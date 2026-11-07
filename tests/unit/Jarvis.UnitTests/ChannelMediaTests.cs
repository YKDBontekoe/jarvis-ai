using System.Text.Json;
using Jarvis.Api.Channels;
using Jarvis.Application.Channels;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ChannelMediaTests
{
    private static JsonDocument Webhook(string messagesJson) => JsonDocument.Parse(
        """{"entry":[{"changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"1234567"},"messages":MESSAGES}}]}]}"""
            .Replace("MESSAGES", messagesJson, StringComparison.Ordinal));

    [Fact]
    public void A_whatsapp_photo_becomes_a_media_reference_with_its_caption_as_the_text()
    {
        using var document = Webhook("""
            [{"from":"31612345678","id":"wamid.p1","type":"image",
              "image":{"id":"media-1","mime_type":"image/jpeg","caption":"  What is the total?  "}}]
            """);

        var message = Assert.Single(WhatsAppWebhook.ParseInbound(document.RootElement, "1234567"));

        Assert.Equal("+31612345678", message.Sender);
        Assert.Equal("What is the total?", message.Text);
        var media = Assert.Single(ChannelMedia.Parse(message.MediaJson));
        Assert.Equal(new ChannelMediaRef("image", "media-1", "image/jpeg", "What is the total?"), media);
        Assert.True(ChannelMedia.IsPhoto(media));
    }

    [Fact]
    public void Other_media_is_kept_so_the_owner_gets_an_honest_answer_instead_of_silence()
    {
        using var document = Webhook("""
            [{"from":"31612345678","id":"wamid.a1","type":"audio","audio":{"id":"media-2","mime_type":"audio/ogg; codecs=opus"}},
             {"from":"31612345678","id":"wamid.d1","type":"document","document":{"id":"media-3","mime_type":"application/pdf"}}]
            """);

        var messages = WhatsAppWebhook.ParseInbound(document.RootElement, "1234567").ToArray();

        Assert.Equal(2, messages.Length);
        var audio = Assert.Single(ChannelMedia.Parse(messages[0].MediaJson));
        Assert.Equal("audio", audio.Kind);
        Assert.Equal("audio/ogg", audio.Mime);
        Assert.False(ChannelMedia.IsPhoto(audio));
        Assert.All(messages, message => Assert.Equal(string.Empty, message.Text));
    }

    [Fact]
    public void Media_without_an_id_or_for_another_number_is_ignored_and_text_still_parses()
    {
        using var document = Webhook("""
            [{"from":"3161","id":"wamid.1","type":"image","image":{}},
             {"from":"3161","id":"wamid.2","type":"image","image":{"id":""}},
             {"from":"3161","id":"wamid.3","type":"text","text":{"body":"hello"}}]
            """);

        var messages = WhatsAppWebhook.ParseInbound(document.RootElement, "1234567").ToArray();
        var textOnly = WhatsAppWebhook.ParseMessages(document.RootElement, "1234567").ToArray();

        Assert.Equal("hello", Assert.Single(messages).Text);
        Assert.Equal([("+3161", "hello", "wamid.3")], textOnly);
        Assert.Empty(WhatsAppWebhook.ParseInbound(document.RootElement, "999"));
    }

    [Fact]
    public void The_text_only_parser_never_returns_media_messages()
    {
        using var document = Webhook("""
            [{"from":"3161","id":"w1","type":"image","image":{"id":"m","mime_type":"image/png"}}]
            """);

        Assert.Empty(WhatsAppWebhook.ParseMessages(document.RootElement, "1234567"));
    }

    [Theory]
    [InlineData("image", "image/jpeg", true)]
    [InlineData("image", "image/jpg", true)]
    [InlineData("IMAGE", "image/PNG", true)]
    [InlineData("image", "image/webp; charset=x", true)]
    [InlineData("image", "image/gif", false)]
    [InlineData("image", "image/heic", false)]
    [InlineData("sticker", "image/webp", false)]
    [InlineData("document", "application/pdf", false)]
    public void Only_jpeg_png_and_webp_photos_count_as_readable(string kind, string mime, bool readable) =>
        Assert.Equal(readable, ChannelMedia.IsPhoto(new ChannelMediaRef(kind, "id", mime, null)));

    [Fact]
    public void The_reply_names_what_cannot_be_read_and_stays_quiet_when_everything_can()
    {
        var photo = new ChannelMediaRef("image", "1", "image/jpeg", null);
        Assert.Null(ChannelMedia.UnreadableReply([photo]));

        var voice = ChannelMedia.UnreadableReply([new ChannelMediaRef("audio", "2", "audio/ogg", null)]);
        Assert.Contains("voice notes or audio", voice, StringComparison.Ordinal);
        Assert.Contains("JPEG, PNG or WebP", voice, StringComparison.Ordinal);

        var mixed = ChannelMedia.UnreadableReply([photo, new ChannelMediaRef("video", "3", "video/mp4", null),
            new ChannelMediaRef("document", "4", "application/pdf", null)]);
        Assert.Contains("videos or documents", mixed, StringComparison.Ordinal);
    }

    [Fact]
    public void More_photos_than_the_model_takes_are_trimmed_with_a_note()
    {
        var photos = Enumerable.Range(0, ChannelMedia.MaxPhotos + 2)
            .Select(index => new ChannelMediaRef("image", $"id{index}", "image/png", null)).ToArray();

        var reply = ChannelMedia.UnreadableReply(photos);

        Assert.Contains($"up to {ChannelMedia.MaxPhotos} photos", reply, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/jpg", ".jpg")]
    public void A_stored_photo_gets_an_extension_that_matches_its_type(string mime, string extension) =>
        Assert.Equal(extension, ChannelMedia.Extension(mime));

    [Fact]
    public void Media_survives_a_round_trip_through_the_queue_and_bad_json_is_empty()
    {
        var media = new[] { new ChannelMediaRef("image", "m1", "image/png", "caption") };

        Assert.Equal(media, ChannelMedia.Parse(ChannelMedia.Serialize(media)));
        Assert.Null(ChannelMedia.Serialize([]));
        Assert.Empty(ChannelMedia.Parse("{not json"));
        Assert.Empty(ChannelMedia.Parse(null));
    }

    [Theory]
    [InlineData("https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=1", "https://graph.facebook.com/v21.0", true)]
    [InlineData("http://lookaside.fbsbx.com/x", "https://graph.facebook.com/v21.0", false)]
    [InlineData("https://127.0.0.1/x", "https://graph.facebook.com/v21.0", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", "https://graph.facebook.com/v21.0", false)]
    [InlineData("https://localhost/x", "https://graph.facebook.com/v21.0", false)]
    [InlineData("https://user:pw@lookaside.fbsbx.com/x", "https://graph.facebook.com/v21.0", false)]
    [InlineData("file:///etc/passwd", "https://graph.facebook.com/v21.0", false)]
    [InlineData(null, "https://graph.facebook.com/v21.0", false)]
    [InlineData("http://localhost:5198/media/1", "http://localhost:5198/v21.0", true)]
    [InlineData("http://localhost:9999/media/1", "http://localhost:5198/v21.0", false)]
    [InlineData("http://evil.example/media/1", "http://localhost:5198/v21.0", false)]
    public void A_media_address_must_be_https_or_on_the_configured_graph_host(string? url, string graph, bool trusted) =>
        Assert.Equal(trusted, WhatsAppCloudTransport.IsTrustedMediaUrl(url, graph));

    [Fact]
    public void A_signal_photo_keeps_its_attachment_id_and_the_text_stays_the_caption()
    {
        using var document = JsonDocument.Parse("""
            [{"envelope":{"sourceNumber":"+31611111111","timestamp":1727450000000,
               "dataMessage":{"message":"What is this?","timestamp":1727450000000,
                 "attachments":[{"id":"abc123.jpg","contentType":"image/jpeg","filename":"IMG_1.jpg","size":1234}]}}},
             {"envelope":{"sourceNumber":"+31611111111","timestamp":1727450000001,
               "dataMessage":{"timestamp":1727450000001,
                 "attachments":[{"id":"voice9.aac","contentType":"audio/aac"}]}}},
             {"envelope":{"sourceNumber":"+31611111111","timestamp":1727450000002,
               "dataMessage":{"timestamp":1727450000002,"attachments":[]}}}]
            """);

        var messages = SignalReceiver.ParseInbound(document.RootElement).ToArray();

        Assert.Equal(2, messages.Length);
        Assert.Equal("What is this?", messages[0].Text);
        Assert.Equal(new ChannelMediaRef("image", "abc123.jpg", "image/jpeg", null),
            Assert.Single(ChannelMedia.Parse(messages[0].MediaJson)));
        Assert.Equal(string.Empty, messages[1].Text);
        Assert.Equal("audio", Assert.Single(ChannelMedia.Parse(messages[1].MediaJson)).Kind);
        Assert.Equal("+31611111111:1727450000000", messages[0].ExternalId);
    }

    [Fact]
    public void Signal_text_messages_parse_exactly_as_before()
    {
        using var document = JsonDocument.Parse("""
            [{"envelope":{"sourceNumber":"+31611111111","timestamp":1727450000000,
               "dataMessage":{"message":"Remind me to call mom","timestamp":1727450000000}}}]
            """);

        Assert.Equal([("+31611111111", "Remind me to call mom", "+31611111111:1727450000000")],
            SignalReceiver.ParseEnvelopes(document.RootElement).ToArray());
        Assert.Null(Assert.Single(SignalReceiver.ParseInbound(document.RootElement)).MediaJson);
    }
}
