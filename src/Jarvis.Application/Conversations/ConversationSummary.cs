using System.Text;
using System.Text.Json;
using Jarvis.Domain.Conversations;

namespace Jarvis.Application.Conversations;

/// <summary>A read-only recap of one conversation: what it was about and what is left to do.</summary>
public sealed record ConversationSummary(
    string Summary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<string> ActionItems,
    int MessageCount);

public interface IConversationSummarizer
{
    /// <summary>Summarizes the messages; null when the model is unavailable or returns nothing usable.</summary>
    Task<ConversationSummary?> SummarizeAsync(Guid ownerId, IReadOnlyList<Message> messages,
        CancellationToken cancellationToken);
}

/// <summary>Pure helpers for building the summary request and reading the model's answer.</summary>
public static class ConversationSummaries
{
    /// <summary>Fewer messages than this has nothing worth summarizing.</summary>
    public const int MinimumMessages = 2;
    public const int MaxTranscriptCharacters = 60_000;
    public const int MaxMessageCharacters = 4_000;
    public const int MaxKeyPoints = 6;
    public const int MaxActionItems = 8;
    private const int MaxSummaryCharacters = 1_200;
    private const int MaxItemCharacters = 200;

    /// <summary>
    /// Renders user and assistant messages, oldest first as stored, as "User:" / "Jarvis:" lines. When the whole conversation does not fit,
    /// the newest messages win and a marker says older ones were left out.
    /// </summary>
    public static string BuildTranscript(IReadOnlyList<Message> messages, int maxCharacters = MaxTranscriptCharacters)
    {
        var lines = new List<string>();
        var used = 0;
        var truncated = false;
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            var speaker = message.Role switch
            {
                "user" => "User",
                "assistant" => "Jarvis",
                _ => null
            };
            var content = message.Content.Trim();
            if (speaker is null || content.Length == 0) continue;
            if (content.Length > MaxMessageCharacters) content = content[..MaxMessageCharacters] + " …";
            var line = $"{speaker}: {content}";
            if (used + line.Length > maxCharacters)
            {
                truncated = true;
                break;
            }

            lines.Add(line);
            used += line.Length + 2;
        }

        lines.Reverse();
        var builder = new StringBuilder();
        if (truncated) builder.Append("[Earlier messages left out]\n\n");
        builder.Append(string.Join("\n\n", lines));
        return builder.ToString();
    }

    /// <summary>Counts the messages <see cref="BuildTranscript"/> would consider.</summary>
    public static int CountSpeakerMessages(IReadOnlyList<Message> messages) =>
        messages.Count(message => message.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(message.Content));

    /// <summary>
    /// Reads <c>{"summary": "...", "key_points": [...], "action_items": [...]}</c>, tolerating a Markdown fence or
    /// prose around the object. Returns null when there is no usable summary.
    /// </summary>
    public static ConversationSummary? Parse(string? text, int messageCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var summary = ReadString(root, "summary", MaxSummaryCharacters);
            if (summary is null) return null;
            return new ConversationSummary(
                summary,
                ReadList(root, "key_points", MaxKeyPoints),
                ReadList(root, "action_items", MaxActionItems),
                messageCount);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > maxLength ? text[..maxLength].TrimEnd() + "…" : text;
    }

    private static IReadOnlyList<string> ReadList(JsonElement root, string name, int maxItems)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.EnumerateArray())
        {
            if (items.Count == maxItems) break;
            if (item.ValueKind != JsonValueKind.String) continue;
            var text = item.GetString()?.Trim().TrimStart('-', '*', '•').Trim();
            if (string.IsNullOrEmpty(text)) continue;
            if (text.Length > MaxItemCharacters) text = text[..MaxItemCharacters].TrimEnd() + "…";
            if (seen.Add(text)) items.Add(text);
        }

        return items;
    }
}
