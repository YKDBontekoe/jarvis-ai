using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Conversations;

/// <summary>What the conversation list shows under a title: the newest message and where the chat came from.</summary>
public sealed record ConversationPreview(
    Guid ConversationId,
    string? Role,
    string? Snippet,
    DateTimeOffset? At,
    string? Origin,
    Guid? OriginId);

public interface IConversationPreviews
{
    /// <summary>
    /// The newest user or assistant message of each listed conversation the owner has, plus the reminder that started
    /// it, if any. Conversations the owner does not own are left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ConversationPreview>> ListPreviewsAsync(Guid ownerId,
        IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken);
}

public static partial class ConversationSnippets
{
    public const int MaxLength = 160;
    public const string OriginReminder = "reminder";

    /// <summary>One plain line for a list row: Markdown, code blocks and extra whitespace removed, cut on a word.</summary>
    public static string? Build(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var text = FencedCode().Replace(content, " ");
        text = Link().Replace(text, "$1");
        text = Image().Replace(text, " ");
        text = LinePrefix().Replace(text, "");
        text = Emphasis().Replace(text, "");
        text = Whitespace().Replace(text, " ").Trim();
        if (text.Length == 0) return null;
        if (text.Length <= MaxLength) return text;
        var cut = text.LastIndexOf(' ', MaxLength - 1);
        var head = text[..(cut > MaxLength / 2 ? cut : MaxLength - 1)].TrimEnd(' ', ',', '.', ';', ':');
        return new StringBuilder(head).Append('…').ToString();
    }

    [GeneratedRegex(@"```[\s\S]*?(```|$)")]
    private static partial Regex FencedCode();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^\s{0,3}(#{1,6}\s+|>\s?|[-*+]\s+|\d+\.\s+)")]
    private static partial Regex LinePrefix();

    [GeneratedRegex(@"(\*\*|__|\*|`|~~)")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
