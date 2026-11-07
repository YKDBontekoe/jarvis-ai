using System.Text.Json;
using Jarvis.Application.Conversations;

namespace Jarvis.Application.Channels;

/// <summary>A photo or other media item that arrived with a channel message, not yet downloaded.</summary>
/// <param name="Kind">The platform's own name for it: image, audio, video, document, sticker.</param>
/// <param name="Id">The platform's media id, used to download it with the connection's own token.</param>
public sealed record ChannelMediaRef(string Kind, string Id, string Mime, string? Caption);

public static class ChannelMedia
{
    /// <summary>At most this many photos go to the model for one message, the same limit as in the app.</summary>
    public const int MaxPhotos = MessageAttachments.MaxPerMessage;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IReadOnlyList<ChannelMediaRef> media) =>
        media.Count == 0 ? null : JsonSerializer.Serialize(media, JsonOptions);

    public static IReadOnlyList<ChannelMediaRef> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<ChannelMediaRef[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>A photo Jarvis can show to the model (JPEG, PNG or WebP, as in the app).</summary>
    public static bool IsPhoto(ChannelMediaRef media) =>
        media.Kind.Equals("image", StringComparison.OrdinalIgnoreCase) &&
        MessageAttachments.IsSupportedImage(NormalizeMime(media.Mime));

    public static string NormalizeMime(string? mime)
    {
        var type = (mime ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return type == "image/jpg" ? "image/jpeg" : type;
    }

    /// <summary>The file extension a stored photo of this type gets, so the upload's type and name agree.</summary>
    public static string Extension(string mime) => NormalizeMime(mime) switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };

    /// <summary>The reply for media Jarvis cannot read, or null when everything sent can be read.</summary>
    public static string? UnreadableReply(IReadOnlyList<ChannelMediaRef> media)
    {
        var unreadable = media.Where(item => !IsPhoto(item)).ToList();
        if (unreadable.Count == 0 && media.Count(IsPhoto) <= MaxPhotos) return null;
        var parts = new List<string>();
        if (unreadable.Count > 0)
        {
            var kinds = unreadable.Select(item => KindName(item.Kind)).Distinct().ToArray();
            parts.Add($"I can read photos (JPEG, PNG or WebP) here, but not {string.Join(" or ", kinds)} yet.");
        }
        if (media.Count(IsPhoto) > MaxPhotos)
            parts.Add($"I can look at up to {MaxPhotos} photos in one message, so I only used the first {MaxPhotos}.");
        return string.Join(" ", parts);
    }

    private static string KindName(string kind) => kind.ToLowerInvariant() switch
    {
        "audio" or "voice" => "voice notes or audio",
        "video" => "videos",
        "document" => "documents",
        "sticker" => "stickers",
        "image" => "that photo format",
        var other => other + " files",
    };
}
