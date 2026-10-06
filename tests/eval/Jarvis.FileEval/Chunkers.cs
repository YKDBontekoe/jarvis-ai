using Jarvis.Worker.Files;

namespace Jarvis.FileEval;

/// <summary>A chunk of a document: the text that is indexed plus the span of the document body it covers.</summary>
internal sealed record Chunk(string Content, int Start, int End);

internal static class Chunkers
{
    /// <summary>
    /// "fixed" is the production chunker (FileTextChunker: fixed length, backs up to a newline or space, fixed overlap).
    /// "recursive" packs paragraphs, then lines, then sentences, then words into chunks of at most <c>length</c> characters,
    /// repeating trailing segments of up to <c>overlap</c> characters. "recursive-ctx" additionally prefixes every chunk
    /// with the document title and the nearest preceding heading, so a chunk keeps its context.
    /// </summary>
    public static List<Chunk> Split(string kind, string text, int length, int overlap) => kind switch
    {
        "fixed" => Fixed(text, length, overlap),
        "recursive" => Recursive(text, length, overlap, withContext: false),
        "recursive-ctx" => Recursive(text, length, overlap, withContext: true),
        _ => throw new ArgumentException($"Unknown chunker {kind}.")
    };

    private static List<Chunk> Fixed(string text, int length, int overlap)
    {
        var options = new FileProcessingOptions
        {
            ChunkLength = length, ChunkOverlap = overlap, MaxChunks = 1_000_000, MaxExtractedCharacters = int.MaxValue
        };
        var chunks = new List<Chunk>();
        var searchFrom = 0;
        foreach (var content in FileTextChunker.SplitIntoChunks(text, options))
        {
            // The worker does not record offsets; chunks are trimmed substrings in order, so find each one again.
            var start = text.IndexOf(content, searchFrom, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("Chunk text not found in its document.");
            chunks.Add(new Chunk(content, start, start + content.Length));
            searchFrom = start + 1;
        }
        return chunks;
    }

    private static readonly string[] Separators = ["\n\n", "\n", ". ", " "];

    private static List<Chunk> Recursive(string text, int length, int overlap, bool withContext)
    {
        var spans = new List<(int Start, int End)>();
        SplitSpans(text, 0, text.Length, length, 0, spans);

        var title = text.StartsWith("# ", StringComparison.Ordinal) ? text[2..text.IndexOf('\n')] : string.Empty;
        var chunks = new List<Chunk>();
        var i = 0;
        while (i < spans.Count)
        {
            var start = spans[i].Start;
            var m = i + 1;
            while (m < spans.Count && spans[m].End - start <= length) m++;
            var end = spans[m - 1].End;
            var (trimmedStart, trimmedEnd) = Trim(text, start, end);
            if (trimmedEnd > trimmedStart)
            {
                var body = text[trimmedStart..trimmedEnd];
                var content = withContext ? Prefix(text, trimmedStart, title) + body : body;
                chunks.Add(new Chunk(content, trimmedStart, trimmedEnd));
            }
            if (m >= spans.Count) break;
            var next = m;
            while (next > i + 1 && spans[m - 1].End - spans[next - 1].Start <= overlap) next--;
            i = next;
        }
        return chunks;
    }

    private static string Prefix(string text, int chunkStart, string title)
    {
        var heading = text.LastIndexOf("\n## ", Math.Min(text.Length - 1, chunkStart + 3), StringComparison.Ordinal);
        var headingText = heading < 0 ? string.Empty : text[(heading + 4)..text.IndexOf('\n', heading + 4)].Trim();
        return headingText.Length == 0 ? $"{title}\n" : $"{title} > {headingText}\n";
    }

    private static (int Start, int End) Trim(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        return (start, end);
    }

    /// <summary>Contiguous spans of at most <paramref name="length"/> characters, split at the coarsest separator that fits.</summary>
    private static void SplitSpans(string text, int start, int end, int length, int level, List<(int, int)> output)
    {
        if (end - start <= length) { output.Add((start, end)); return; }
        if (level >= Separators.Length)
        {
            for (var at = start; at < end; at += length) output.Add((at, Math.Min(end, at + length)));
            return;
        }
        var separator = Separators[level];
        var piece = start;
        while (piece < end)
        {
            var found = text.IndexOf(separator, piece, end - piece, StringComparison.Ordinal);
            var pieceEnd = found < 0 ? end : found + separator.Length;
            SplitSpans(text, piece, pieceEnd, length, level + 1, output);
            piece = pieceEnd;
        }
    }
}
