namespace Jarvis.Worker.Files;

public static class FileTextChunker
{
    public static List<string> SplitIntoChunks(string text, FileProcessingOptions options)
    {
        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length && chunks.Count < options.MaxChunks)
        {
            var end = Math.Min(text.Length, start + options.ChunkLength);
            if (end < text.Length)
            {
                var boundary = text.LastIndexOfAny(['\n', ' '], end - 1, Math.Min(400, end - start));
                if (boundary > start + options.ChunkLength / 2) end = boundary;
            }
            var chunk = text[start..end].Trim();
            if (chunk.Length != 0) chunks.Add(chunk);
            if (end == text.Length) break;
            start = Math.Max(start + 1, end - options.ChunkOverlap);
        }
        return chunks;
    }
}
