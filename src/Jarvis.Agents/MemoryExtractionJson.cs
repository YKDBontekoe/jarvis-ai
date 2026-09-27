namespace Jarvis.Agents;

internal static class MemoryExtractionJson
{
    public static string? UnwrapArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var text = json.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && fence > firstNewline)
                text = text[(firstNewline + 1)..fence].Trim();
        }

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}
