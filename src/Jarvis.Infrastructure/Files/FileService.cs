using System.Security.Cryptography;
using System.Text;
using Jarvis.Application.Files;
using Jarvis.Domain.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Files;

public sealed class FileService(
    IFileRepository repository,
    IObjectStorage objects,
    IFileProcessingScheduler scheduler,
    IFileMalwareScanner malwareScanner,
    IConfiguration configuration,
    ILogger<FileService> logger) : IFileService
{
    private const long DefaultMaxUploadBytes = 20 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, HashSet<string>> AcceptedTypes =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [".pdf"],
            ["text/plain"] = [".txt", ".log"],
            ["text/markdown"] = [".md", ".markdown"],
            ["text/csv"] = [".csv"],
            ["application/json"] = [".json"],
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/png"] = [".png"],
            ["image/webp"] = [".webp"]
        };

    private readonly long _maxUploadBytes = ResolveMaxUploadBytes(configuration);

    public async Task<StoredFile> UploadAsync(Guid ownerId, string fileName, string contentType, long length,
        Stream content, CancellationToken cancellationToken)
    {
        var safeName = NormalizeFileName(fileName);
        var normalizedType = contentType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AcceptedTypes.TryGetValue(normalizedType, out var extensions) ||
            !extensions.Contains(Path.GetExtension(safeName)))
            throw new ArgumentException("File type is not supported.", nameof(contentType));
        if (length <= 0 || length > _maxUploadBytes)
            throw new ArgumentOutOfRangeException(nameof(length), $"File size must be between 1 and {_maxUploadBytes} bytes.");
        if (!content.CanSeek)
            throw new ArgumentException("The upload stream must support seeking.", nameof(content));

        content.Position = 0;
        await ValidateContentSignatureAsync(content, normalizedType, cancellationToken);
        content.Position = 0;
        var sha256 = await HashAndValidateLengthAsync(content, length, normalizedType, cancellationToken);
        content.Position = 0;
        await malwareScanner.ScanAsync(content, cancellationToken);
        content.Position = 0;

        var id = Guid.CreateVersion7();
        var objectKey = $"{ownerId:D}/{id:D}";
        await objects.PutAsync(objectKey, content, normalizedType, cancellationToken);
        var processingStatus = FileIndexing.IsIndexable(normalizedType) ? "queued" : "uploaded";
        var file = new StoredFile(id, ownerId, objectKey, safeName, normalizedType, length,
            sha256, DateTimeOffset.UtcNow, processingStatus);
        StoredFile stored;
        try
        {
            stored = await repository.CreateAsync(file, cancellationToken);
        }
        catch
        {
            try
            {
                await objects.DeleteAsync(objectKey, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError(cleanupException, "Could not remove unreferenced file object {ObjectKey} after metadata creation failed.", objectKey);
            }
            throw;
        }

        if (processingStatus == "queued")
        {
            try
            {
                await scheduler.ScheduleAsync(stored.Id, ownerId, cancellationToken);
                await repository.MarkProcessingScheduleDispatchedAsync(stored.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "File {FileId} remains queued for Temporal scheduling recovery.", stored.Id);
            }
        }
        return stored;
    }

    public Task<IReadOnlyList<StoredFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        repository.ListAsync(ownerId, cancellationToken);

    public async Task<StoredFile?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var file = await repository.GetAsync(id, ownerId, cancellationToken);
        return file is null || file.ProcessingStatus == "deleting" ? null : file;
    }

    public async Task<(StoredFile File, Stream Content)?> OpenReadAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var file = await repository.GetAsync(id, ownerId, cancellationToken);
        if (file is null || file.ProcessingStatus == "deleting") return null;
        var content = await objects.GetAsync(file.ObjectKey, cancellationToken);
        return content is null ? null : (file, content);
    }

    public async Task<bool> RetryIndexingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var file = await repository.GetAsync(id, ownerId, cancellationToken);
        if (file is null || file.ProcessingStatus == "deleting" || !FileIndexing.IsIndexable(file.ContentType))
            return false;
        if (!await repository.RequeueForProcessingAsync(id, ownerId, cancellationToken)) return false;
        try
        {
            await scheduler.ScheduleAsync(id, ownerId, cancellationToken);
            await repository.MarkProcessingScheduleDispatchedAsync(id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "File {FileId} remains queued for Temporal indexing recovery.", id);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var file = await repository.GetAsync(id, ownerId, cancellationToken);
        if (file is null) return false;
        if (!await repository.MarkDeletingAsync(id, ownerId, cancellationToken)) return false;
        try
        {
            await scheduler.CancelAsync(id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "File {FileId} was marked deleting; Temporal will stop processing if the workflow is still running.", id);
        }
        await objects.DeleteAsync(file.ObjectKey, cancellationToken);
        await repository.DeleteAsync(id, ownerId, cancellationToken);
        return true;
    }

    private static string NormalizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A file name is required.", nameof(fileName));
        var normalized = Path.GetFileName(fileName.Replace('\\', '/')).Normalize(NormalizationForm.FormC).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255 || normalized.Any(char.IsControl))
            throw new ArgumentException("File name must contain 1 to 255 printable characters.", nameof(fileName));
        return normalized;
    }

    private static long ResolveMaxUploadBytes(IConfiguration configuration)
    {
        var configured = configuration["ObjectStorage:MaxUploadBytes"];
        if (configured is null) return DefaultMaxUploadBytes;
        if (!long.TryParse(configured, out var max))
            throw new InvalidOperationException("ObjectStorage:MaxUploadBytes must be an integer.");
        if (max <= 0)
            throw new InvalidOperationException("ObjectStorage:MaxUploadBytes must be greater than zero.");
        return max;
    }

    private async Task<string> HashAndValidateLengthAsync(Stream content, long expectedLength, string contentType,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long actualLength = 0;
        var validateText = contentType.StartsWith("text/", StringComparison.Ordinal) || contentType == "application/json";
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            actualLength = checked(actualLength + read);
            if (actualLength > _maxUploadBytes || actualLength > expectedLength)
                throw new ArgumentOutOfRangeException(nameof(expectedLength),
                    $"File size must not exceed {_maxUploadBytes} bytes and must match the declared upload length.");
            if (validateText && buffer.AsSpan(0, read).Contains((byte)0))
                throw new ArgumentException("Text files cannot contain null bytes.", nameof(content));
            hash.AppendData(buffer, 0, read);
        }
        if (actualLength != expectedLength)
            throw new ArgumentException("File content length does not match the declared upload length.", nameof(content));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task ValidateContentSignatureAsync(Stream content, string contentType,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[12];
        var count = await content.ReadAsync(prefix, cancellationToken);
        var valid = contentType switch
        {
            "application/pdf" => count >= 5 && prefix.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            "image/png" => count >= 8 && prefix.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => count >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF,
            "image/webp" => count >= 12 && prefix.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                prefix.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => !prefix.AsSpan(0, count).Contains((byte)0)
        };
        if (!valid)
            throw new ArgumentException("File content does not match its declared type.", nameof(contentType));
    }
}
