namespace Jarvis.Application.Files;

public static class FileIndexing
{
    public const int QueuedDispatchStaleMinutes = 15;

    public static bool IsIndexable(string contentType) =>
        contentType is "application/pdf" or "application/json" or "application/yaml" or "image/jpeg" or "image/png" or
            "image/webp" || OfficeText.IsOffice(contentType) || contentType.StartsWith("text/", StringComparison.Ordinal);
}
