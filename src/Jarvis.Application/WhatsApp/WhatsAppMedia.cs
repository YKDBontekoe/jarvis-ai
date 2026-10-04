using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.Application.WhatsApp;

/// <summary>The message a WhatsApp reply quotes.</summary>
public sealed record WhatsAppQuote(string? Author, string Text);

/// <summary>
/// A photo, sticker, video, voice note, document, location, contact or poll attached to a read-along message.
/// <see cref="HasContent"/> means the bytes were stored and can be loaded separately.
/// </summary>
public sealed record WhatsAppMedia(
    string Kind,
    string? Mime,
    string? FileName,
    int? Seconds,
    int? Width,
    int? Height,
    bool Animated,
    bool Voice,
    double? Latitude,
    double? Longitude,
    string? Place,
    string? ContactName,
    IReadOnlyList<string>? PollOptions,
    bool HasContent);

/// <summary>Media as the bridge described it, before the file bytes are checked.</summary>
public sealed record WhatsAppIncomingMedia(
    string? Kind,
    string? Mime = null,
    string? FileName = null,
    int? Seconds = null,
    int? Width = null,
    int? Height = null,
    bool Animated = false,
    bool Voice = false,
    double? Latitude = null,
    double? Longitude = null,
    string? Place = null,
    string? ContactName = null,
    IReadOnlyList<string>? PollOptions = null,
    bool HasContent = false);

/// <summary>What to store for one observed message: the JSON description, and the bytes when they checked out.</summary>
public sealed record WhatsAppStoredMedia(string Json, byte[]? Content, string? ContentType, WhatsAppMedia? Media,
    WhatsAppQuote? Quote);

public static class WhatsAppMediaCodec
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "image", "sticker", "video", "gif", "audio", "document", "location", "contact", "poll"
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The line the agent sees: the stored text, plus the quote when this message replies to one.</summary>
    public static string ForAgent(WhatsAppChatMessage message)
    {
        var text = message.Text.ReplaceLineEndings(" ");
        if (message.Quote is { Text: { Length: > 0 } quoted })
            text += " (replying to: " + quoted.ReplaceLineEndings(" ") + ")";
        return text;
    }

    public static WhatsAppStoredMedia? Prepare(WhatsAppIncomingMedia? incoming, WhatsAppQuote? quote, byte[]? content)
    {
        var media = Clean(incoming);
        var cleanQuote = CleanQuote(quote);
        string? contentType = null;
        byte[]? stored = null;
        if (media is { HasContent: true } && content is { Length: > 0 })
        {
            var accepted = WhatsAppMediaBytes.Accept(media.Kind, media.Mime, content);
            if (accepted is null) media = media with { HasContent = false };
            else (stored, contentType) = accepted.Value;
        }
        else if (media is { HasContent: true })
        {
            media = media with { HasContent = false };
        }
        if (media is null && cleanQuote is null) return null;
        return new WhatsAppStoredMedia(JsonSerializer.Serialize(new Stored(media, cleanQuote), Json), stored,
            contentType, media, cleanQuote);
    }

    public static (WhatsAppMedia? Media, WhatsAppQuote? Quote) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json, Json);
            if (stored is null) return (null, null);
            return (Clean(stored.Media), CleanQuote(stored.Quote));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static WhatsAppMedia? Clean(WhatsAppMedia? media) => media is null ? null : Clean(new WhatsAppIncomingMedia(
        media.Kind, media.Mime, media.FileName, media.Seconds, media.Width, media.Height, media.Animated, media.Voice,
        media.Latitude, media.Longitude, media.Place, media.ContactName, media.PollOptions, media.HasContent));

    private static WhatsAppMedia? Clean(WhatsAppIncomingMedia? incoming)
    {
        if (incoming?.Kind is not { } kind || !Kinds.Contains(kind)) return null;
        var options = incoming.PollOptions?
            .Select(option => Compact(option, 80))
            .Where(option => option is not null)
            .Select(option => option!)
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        var latitude = kind == "location" ? Coordinate(incoming.Latitude, 90) : null;
        var longitude = kind == "location" ? Coordinate(incoming.Longitude, 180) : null;
        return new WhatsAppMedia(kind, Mime(incoming.Mime), FileName(incoming.FileName),
            Range(incoming.Seconds, 86_400), Range(incoming.Width, 20_000), Range(incoming.Height, 20_000),
            incoming.Animated && kind is "sticker" or "gif", incoming.Voice && kind == "audio",
            latitude, longitude, kind == "location" ? Compact(incoming.Place, 120) : null,
            kind == "contact" ? Compact(incoming.ContactName, 80) : null,
            kind == "poll" && options is { Length: > 0 } ? options : null, incoming.HasContent);
    }

    private static WhatsAppQuote? CleanQuote(WhatsAppQuote? quote)
    {
        var text = Compact(quote?.Text, 300);
        return text is null ? null : new WhatsAppQuote(Compact(quote?.Author, 80), text);
    }

    private static string? Compact(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= limit ? clean : clean[..limit].TrimEnd();
    }

    private static string? FileName(string? value)
    {
        var clean = Compact(value, 120);
        if (clean is null) return null;
        var baseName = clean.Split('/', '\\')[^1];
        var safe = new string(baseName.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '.' or '_' or '-' or '(' or ')').ToArray()).Trim();
        return safe.Length == 0 ? null : safe.Length <= 120 ? safe : safe[..120].TrimEnd();
    }

    private static string? Mime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var mime = value.Split(';')[0].Trim().ToLowerInvariant();
        return mime.Length <= 100 && mime.Contains('/') && mime.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '/' or '.' or '+' or '-')
            ? mime : null;
    }

    private static int? Range(int? value, int max) => value is > 0 and var number && number <= max ? number : null;

    private static double? Coordinate(double? value, double limit) =>
        value is { } number && number >= -limit && number <= limit ? Math.Round(number, 6) : null;

    private sealed record Stored(WhatsAppMedia? Media, WhatsAppQuote? Quote);
}

/// <summary>Checks that stored bytes match the kind. The declared type alone is not enough.</summary>
public static class WhatsAppMediaBytes
{
    public const int MaxBytes = 8_000_000;

    private static readonly HashSet<string> Office = new(StringComparer.Ordinal)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/zip"
    };

    public static (byte[] Bytes, string Type)? Accept(string kind, string? declaredMime, byte[] bytes)
    {
        var limit = kind is "sticker" ? 1_500_000 : kind is "video" or "gif" ? 200_000 : MaxBytes;
        if (bytes.Length < 8 || bytes.Length > limit) return null;
        var sniffed = Sniff(bytes);
        var declared = declaredMime?.Split(';')[0].Trim().ToLowerInvariant();
        return kind switch
        {
            "image" when sniffed is "image/jpeg" or "image/png" or "image/webp" => (bytes, sniffed),
            "sticker" when sniffed == "image/webp" => (bytes, "image/webp"),
            "video" or "gif" when sniffed == "image/jpeg" => (bytes, "image/jpeg"),
            "audio" when sniffed is "audio/ogg" or "audio/mpeg" or "audio/mp4" => (bytes, sniffed),
            "document" when sniffed == "application/pdf" && declared == "application/pdf" => (bytes, declared),
            "document" when sniffed == "application/zip" && declared is not null && Office.Contains(declared) => (bytes, declared),
            "document" when declared == "text/plain" && PlainText(bytes) => (bytes, "text/plain"),
            _ => null
        };
    }

    private static bool PlainText(byte[] bytes)
    {
        if (bytes.Contains((byte)0)) return false;
        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 64)).TrimStart().ToLowerInvariant();
        return !head.StartsWith('<');
    }

    private static string? Sniff(byte[] bytes)
    {
        if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47) return "image/png";
        if (Starts(bytes, "RIFF") && bytes.Length >= 12 && Starts(bytes, "WEBP", 8)) return "image/webp";
        if (Starts(bytes, "OggS")) return "audio/ogg";
        if (Starts(bytes, "ID3")) return "audio/mpeg";
        if (bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0) return "audio/mpeg";
        if (bytes.Length >= 12 && Starts(bytes, "ftyp", 4)) return "audio/mp4";
        if (Starts(bytes, "%PDF-")) return "application/pdf";
        if (bytes[0] == 0x50 && bytes[1] == 0x4b && bytes[2] == 0x03 && bytes[3] == 0x04) return "application/zip";
        return null;
    }

    private static bool Starts(byte[] bytes, string text, int offset = 0)
    {
        if (bytes.Length < offset + text.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (bytes[offset + i] != text[i]) return false;
        return true;
    }
}
