using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Channels;

namespace Jarvis.Api.Channels;

public static class WhatsAppWebhook
{
    /// <summary>Verifies Meta's X-Hub-Signature-256 HMAC of the raw request body.</summary>
    public static bool HasValidSignature(string? header, ReadOnlySpan<byte> body, string? appSecret)
    {
        if (string.IsNullOrEmpty(appSecret) || header is null ||
            !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(header.AsSpan(7)); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    /// <summary>Extracts text and button replies addressed to the connection's phone number id.</summary>
    public static IEnumerable<(string Sender, string Text, string ExternalId)> ParseMessages(JsonElement root,
        string phoneNumberId) =>
        ParseInbound(root, phoneNumberId).Where(message => message.MediaJson is null)
            .Select(message => (message.Sender, message.Text, message.ExternalId));

    /// <summary>One inbound message: its text (or a photo's caption) and any media that came with it.</summary>
    public sealed record Inbound(string Sender, string Text, string ExternalId, string? MediaJson);

    private static readonly string[] MediaTypes = ["image", "audio", "video", "document", "sticker"];

    /// <summary>
    /// Text, button replies and media addressed to the connection's phone number id. Media used to be dropped
    /// without a word; it is now handed on with its platform id so the router can read photos and say honestly what
    /// it cannot read.
    /// </summary>
    public static IEnumerable<Inbound> ParseInbound(JsonElement root, string phoneNumberId)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) yield break;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                var target = value.TryGetProperty("metadata", out var metadata) &&
                             metadata.TryGetProperty("phone_number_id", out var id)
                    ? id.GetString()
                    : null;
                if (!string.Equals(target, phoneNumberId, StringComparison.Ordinal)) continue;
                if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var message in messages.EnumerateArray())
                {
                    var from = message.TryGetProperty("from", out var sender) ? sender.GetString() : null;
                    var messageId = message.TryGetProperty("id", out var wamid) ? wamid.GetString() : null;
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(messageId)) continue;
                    var text = ReadText(message);
                    if (text is not null)
                    {
                        yield return new Inbound(ChannelAddresses.Normalize(from), text, messageId!, null);
                        continue;
                    }
                    if (ReadMedia(message) is { } media)
                        yield return new Inbound(ChannelAddresses.Normalize(from), media.Caption ?? string.Empty,
                            messageId!, ChannelMedia.Serialize([media]));
                }
            }
        }
    }

    private static ChannelMediaRef? ReadMedia(JsonElement message)
    {
        var type = message.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (type is null || !MediaTypes.Contains(type, StringComparer.Ordinal) ||
            !message.TryGetProperty(type, out var body) || body.ValueKind != JsonValueKind.Object)
            return null;
        var id = body.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) return null;
        var mime = body.TryGetProperty("mime_type", out var mimeElement) ? mimeElement.GetString() : null;
        var caption = body.TryGetProperty("caption", out var captionElement) ? captionElement.GetString() : null;
        return new ChannelMediaRef(type, id, ChannelMedia.NormalizeMime(mime),
            string.IsNullOrWhiteSpace(caption) ? null : caption.Trim());
    }

    private static string? ReadText(JsonElement message)
    {
        var type = message.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        return type switch
        {
            "text" when message.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body) =>
                body.GetString(),
            "button" when message.TryGetProperty("button", out var button) && button.TryGetProperty("text", out var label) =>
                label.GetString(),
            "interactive" when message.TryGetProperty("interactive", out var interactive) =>
                interactive.TryGetProperty("button_reply", out var reply) && reply.TryGetProperty("title", out var title)
                    ? title.GetString()
                    : interactive.TryGetProperty("list_reply", out var list) && list.TryGetProperty("title", out var item)
                        ? item.GetString()
                        : null,
            _ => null
        };
    }
}
