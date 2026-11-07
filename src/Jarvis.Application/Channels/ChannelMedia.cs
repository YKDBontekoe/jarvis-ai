using System.Text.Json;
using Jarvis.Application.Conversations;

namespace Jarvis.Application.Channels;

/// <summary>A photo or other media item that arrived with a channel message, not yet downloaded.</summary>
/// <param name="Kind">The platform's own name for it: image, audio, video, document, sticker.</param>
/// <param name="Id">The platform's media id, used to download it with the connection's own token.</param>
public sealed record ChannelMediaRef(string Kind, string Id, string Mime, string? Caption, string? FileName = null);

public static class ChannelMedia
{
    /// <summary>At most this many photos go to the model for one message, the same limit as in the app.</summary>
    public const int MaxPhotos = MessageAttachments.MaxPerMessage;

    /// <summary>A file sent in chat is stored like an upload; this is the largest one Jarvis downloads.</summary>
    public const long MaxDocumentBytes = 15L * 1024 * 1024;

    /// <summary>At most this many files are stored from one message.</summary>
    public const int MaxDocuments = 3;

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

    /// <summary>A file (PDF, Word, text, ...) Jarvis tries to store and read; the upload check decides the type.</summary>
    public static bool IsDocument(ChannelMediaRef media) =>
        media.Kind.Equals("document", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A safe name for a stored file: the sender's own name when it is usable (one line, no folders, capped), and an
    /// extension that agrees with the type the platform reported.
    /// </summary>
    public static string DocumentName(ChannelMediaRef media, string channelLabel, int index)
    {
        var raw = new string((media.FileName ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
        var name = Path.GetFileName(raw.Replace('\\', '/')).Trim();
        if (name.Length > 100) name = name[^100..].TrimStart('.', ' ');
        var extension = DocumentExtension(media.Mime);
        if (name.Length == 0) return $"{channelLabel.ToLowerInvariant()}-file-{index}{extension}";
        if (extension.Length > 0 && !Path.GetExtension(name).Equals(extension, StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(name).Length == 0)
            name += extension;
        return name;
    }

    private static string DocumentExtension(string? mime) => NormalizeMime(mime) switch
    {
        "application/pdf" => ".pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        "text/plain" => ".txt",
        "text/csv" => ".csv",
        "text/tab-separated-values" => ".tsv",
        "text/markdown" => ".md",
        "text/calendar" => ".ics",
        "application/json" => ".json",
        _ => string.Empty,
    };

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
        var unreadable = media.Where(item => !IsPhoto(item) && !IsDocument(item)).ToList();
        if (unreadable.Count == 0 && media.Count(IsPhoto) <= MaxPhotos) return null;
        var parts = new List<string>();
        if (unreadable.Count > 0)
        {
            var kinds = unreadable.Select(item => KindName(item.Kind)).Distinct().ToArray();
            parts.Add($"I can read photos (JPEG, PNG or WebP) and files such as PDF or Word here, but not {string.Join(" or ", kinds)} yet.");
        }
        if (media.Count(IsPhoto) > MaxPhotos)
            parts.Add($"I can look at up to {MaxPhotos} photos in one message, so I only used the first {MaxPhotos}.");
        return string.Join(" ", parts);
    }

    private static string KindName(string kind) => kind.ToLowerInvariant() switch
    {
        "audio" or "voice" => "voice notes or audio",
        "video" => "videos",
        "sticker" => "stickers",
        "image" => "that photo format",
        var other => other + " files",
    };
}
