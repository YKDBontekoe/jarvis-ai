using System.Text.Json;

namespace Jarvis.Application.Conversations;

/// <summary>A photo the owner sent with a chat message, stored as one of their own files.</summary>
public sealed record MessageAttachment(Guid FileId, string FileName, string ContentType);

public static class MessageAttachments
{
    /// <summary>At most this many photos go with one message, matching the Codex per-request image limit.</summary>
    public const int MaxPerMessage = 4;

    /// <summary>Largest photo sent to the model; larger files are refused before the turn starts.</summary>
    public const long MaxImageBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsSupportedImage(string contentType) =>
        contentType is "image/jpeg" or "image/png" or "image/webp";

    public static string? Serialize(IReadOnlyList<MessageAttachment>? attachments) =>
        attachments is { Count: > 0 } ? JsonSerializer.Serialize(attachments, JsonOptions) : null;

    public static IReadOnlyList<MessageAttachment> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<MessageAttachment[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Text stored for a message that only carries photos.</summary>
    public static string DefaultContent(int count) =>
        count == 1 ? "Shared a photo." : $"Shared {count} photos.";
}
