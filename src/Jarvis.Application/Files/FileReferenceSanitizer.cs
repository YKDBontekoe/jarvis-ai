using System.Text.RegularExpressions;

namespace Jarvis.Application.Files;

public static partial class FileReferenceSanitizer
{
    private static readonly string[] InjectionPrefixes =
    [
        "system:", "assistant:", "developer:", "ignore previous", "ignore all previous",
        "disregard previous", "you are now", "new instructions:", "override:"
    ];

    public static string SanitizeExcerpt(string? content, int maxLength = 2_400)
    {
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                kept.Add(string.Empty);
                continue;
            }

            var lower = trimmed.ToLowerInvariant();
            if (InjectionPrefixes.Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal)))
                kept.Add("[removed untrusted instruction-like text]");
            else
                kept.Add(trimmed);
        }

        var text = string.Join('\n', kept).Trim();
        if (text.Length <= maxLength) return text;
        return text[..Math.Max(0, maxLength - 1)] + "…";
    }

    public static string StripControlCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return ControlChars().Replace(value, string.Empty);
    }

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]")]
    private static partial Regex ControlChars();
}
