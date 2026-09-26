namespace Jarvis.Application.Files;

public static class FileIndexing
{
    public static bool IsIndexable(string contentType) =>
        contentType is "application/pdf" or "application/json" or "image/jpeg" or "image/png" or "image/webp" ||
        contentType.StartsWith("text/", StringComparison.Ordinal);
}
