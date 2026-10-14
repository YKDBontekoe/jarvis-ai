using System.Text.RegularExpressions;

namespace Jarvis.Worker.Files;

/// <summary>One indexed passage: trimmed text, its offsets in the extracted text, page, and nearest heading.</summary>
public sealed record TextChunk(string Content, int StartOffset, int EndOffset, int? PageNumber, string? Heading);

/// <summary>The chunks of a file, and whether text was left out because the chunk limit was reached.</summary>
public sealed record ChunkingResult(IReadOnlyList<TextChunk> Chunks, bool Truncated);

public static partial class FileTextChunker
{
    private const int MaxHeadingLength = 200;

    public static List<string> SplitIntoChunks(string text, FileProcessingOptions options) =>
        Split(text, options).Chunks.Select(chunk => chunk.Content).ToList();

    /// <summary>
    /// Splits text into overlapping windows of at most <see cref="FileProcessingOptions.ChunkLength"/> characters.
    /// A window ends at the last paragraph break in its second half when there is one, else at a sentence end, line
    /// break or space, so passages are cut where the text itself pauses. <paramref name="pageStarts"/> holds the
    /// offset where each page begins (page 1 first); a chunk gets the page its first character is on.
    /// </summary>
    public static ChunkingResult Split(string text, FileProcessingOptions options, IReadOnlyList<int>? pageStarts = null)
    {
        var headings = FindHeadings(text);
        var chunks = new List<TextChunk>();
        var start = 0;
        var covered = 0;
        var truncated = false;
        while (start < text.Length)
        {
            if (chunks.Count >= options.MaxChunks)
            {
                // Overlap means the next window starts inside text that is already indexed.
                truncated = !string.IsNullOrWhiteSpace(text[covered..]);
                break;
            }
            var end = Math.Min(text.Length, start + options.ChunkLength);
            if (end < text.Length) end = Boundary(text, start, end, options.ChunkLength);

            var leading = 0;
            while (start + leading < end && char.IsWhiteSpace(text[start + leading])) leading++;
            var trailing = 0;
            while (end - trailing > start + leading && char.IsWhiteSpace(text[end - trailing - 1])) trailing++;
            var contentStart = start + leading;
            var contentEnd = end - trailing;
            if (contentEnd > contentStart)
                chunks.Add(new TextChunk(text[contentStart..contentEnd], contentStart, contentEnd,
                    PageAt(pageStarts, contentStart), HeadingAt(headings, contentStart)));
            covered = end;
            if (end == text.Length) break;
            start = Math.Max(start + 1, end - options.ChunkOverlap);
        }
        return new ChunkingResult(chunks, truncated);
    }

    private static int Boundary(string text, int start, int end, int chunkLength)
    {
        // Only the second half of the window is searched, so a boundary never makes a chunk shorter than half the length.
        var earliest = start + chunkLength / 2;
        var paragraph = text.LastIndexOf("\n\n", end - 1, end - earliest, StringComparison.Ordinal);
        if (paragraph > earliest) return paragraph;
        var nearest = Math.Max(earliest, end - 400);
        for (var index = end - 2; index > nearest; index--)
            if (text[index] is '.' or '!' or '?' && char.IsWhiteSpace(text[index + 1])) return index + 1;
        var space = text.LastIndexOfAny(['\n', ' '], end - 1, end - nearest);
        return space > nearest ? space : end;
    }

    private static int? PageAt(IReadOnlyList<int>? pageStarts, int offset)
    {
        if (pageStarts is null || pageStarts.Count == 0) return null;
        var page = 0;
        while (page + 1 < pageStarts.Count && pageStarts[page + 1] <= offset) page++;
        return page + 1;
    }

    private static string? HeadingAt(List<(int Offset, string Text)> headings, int offset)
    {
        string? current = null;
        foreach (var heading in headings)
        {
            if (heading.Offset > offset) break;
            current = heading.Text;
        }
        return current;
    }

    /// <summary>Markdown headings (# to ######) with the offset of their line; other formats have none.</summary>
    private static List<(int Offset, string Text)> FindHeadings(string text) =>
        HeadingPattern().Matches(text)
            .Select(match => (match.Index, Text: match.Groups[1].Value.Trim()))
            .Where(heading => heading.Text.Length > 0)
            .Select(heading => (heading.Index,
                heading.Text.Length <= MaxHeadingLength ? heading.Text : heading.Text[..MaxHeadingLength]))
            .ToList();

    [GeneratedRegex(@"^#{1,6}[ \t]+(.+?)[ \t#]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();
}
