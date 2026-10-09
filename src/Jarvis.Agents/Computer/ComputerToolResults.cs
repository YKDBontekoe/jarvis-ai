using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Computer;

/// <param name="Text">The tool's text output.</param>
/// <param name="Image">The last image the tool returned (a screenshot), if any.</param>
internal sealed record ComputerObservation(string Text, byte[]? Image, string? ImageMediaType, bool IsError);

/// <summary>Reads an MCP tool result in whichever shape it arrives: AI contents, or a serialized CallToolResult.</summary>
internal static class ComputerToolResults
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public static ComputerObservation Read(object? result)
    {
        switch (result)
        {
            case null:
                return new ComputerObservation(string.Empty, null, null, false);
            case string text:
                return new ComputerObservation(text, null, null, false);
            case DataContent data:
                return FromContents([data]);
            case IEnumerable<AIContent> contents:
                return FromContents(contents);
            case JsonElement element:
                return FromJson(element);
            default:
                try
                {
                    return FromJson(JsonSerializer.SerializeToElement(result, result.GetType(), JsonOptions));
                }
                catch (NotSupportedException)
                {
                    return new ComputerObservation(result.ToString() ?? string.Empty, null, null, false);
                }
        }
    }

    private static ComputerObservation FromContents(IEnumerable<AIContent> contents)
    {
        var text = new StringBuilder();
        byte[]? image = null;
        string? mediaType = null;
        foreach (var content in contents)
        {
            if (content is TextContent textContent) AppendLine(text, textContent.Text);
            else if (content is DataContent data && IsSupportedImage(data.MediaType))
            {
                image = data.Data.ToArray();
                mediaType = data.MediaType;
            }
        }

        return new ComputerObservation(text.ToString().TrimEnd(), image, mediaType, false);
    }

    private static ComputerObservation FromJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return new ComputerObservation(element.GetString() ?? string.Empty, null, null, false);
        var items = element.ValueKind switch
        {
            JsonValueKind.Array => element,
            JsonValueKind.Object when element.TryGetProperty("content", out var content) &&
                                      content.ValueKind == JsonValueKind.Array => content,
            _ => default
        };
        if (items.ValueKind != JsonValueKind.Array)
            return new ComputerObservation(element.GetRawText(), null, null, false);

        var isError = element.ValueKind == JsonValueKind.Object &&
                      element.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
        var text = new StringBuilder();
        byte[]? image = null;
        string? mediaType = null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var type = String(item, "type") ?? String(item, "$type");
            if (type == "text")
                AppendLine(text, String(item, "text"));
            else if (type == "image" && String(item, "data") is { } data &&
                     (String(item, "mimeType") ?? String(item, "mediaType")) is { } mime && IsSupportedImage(mime))
            {
                if (TryBase64(data) is { } bytes)
                {
                    image = bytes;
                    mediaType = mime;
                }
            }
            else if (String(item, "uri") is { } uri && uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = new DataContent(uri);
                if (IsSupportedImage(parsed.MediaType))
                {
                    image = parsed.Data.ToArray();
                    mediaType = parsed.MediaType;
                }
            }
        }

        return new ComputerObservation(text.ToString().TrimEnd(), image, mediaType, isError);
    }

    private static bool IsSupportedImage(string? mediaType) =>
        mediaType is not null && ImageTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);

    private static string? String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static byte[]? TryBase64(string data)
    {
        try { return Convert.FromBase64String(data); }
        catch (FormatException) { return null; }
    }

    private static void AppendLine(StringBuilder builder, string? text)
    {
        if (!string.IsNullOrEmpty(text)) builder.AppendLine(text);
    }
}
