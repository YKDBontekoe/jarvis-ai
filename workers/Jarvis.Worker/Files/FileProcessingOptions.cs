using Microsoft.Extensions.Options;

namespace Jarvis.Worker.Files;

public sealed class FileProcessingOptions
{
    public const string SectionName = "FileProcessing";

    public int MaxExtractedCharacters { get; set; } = 300_000;

    /// <summary>About 400 tokens: small enough for a focused embedding and excerpt, large enough to keep a passage whole.</summary>
    public int ChunkLength { get; set; } = 1_600;

    public int ChunkOverlap { get; set; } = 200;

    /// <summary>Enough for <see cref="MaxExtractedCharacters"/> at the default chunk length, so only extraction caps a file.</summary>
    public int MaxChunks { get; set; } = 400;

    public long MaxDownloadBytes { get; set; } = 20 * 1024 * 1024;

    public long MaxImageBytes { get; set; } = 8 * 1024 * 1024;
}

public sealed class FileProcessingOptionsValidator : IValidateOptions<FileProcessingOptions>
{
    public ValidateOptionsResult Validate(string? name, FileProcessingOptions options)
    {
        if (options.MaxExtractedCharacters <= 0)
            return ValidateOptionsResult.Fail($"{nameof(options.MaxExtractedCharacters)} must be positive.");
        if (options.ChunkLength <= 0)
            return ValidateOptionsResult.Fail($"{nameof(options.ChunkLength)} must be positive.");
        if (options.ChunkOverlap < 0 || options.ChunkOverlap >= options.ChunkLength)
            return ValidateOptionsResult.Fail($"{nameof(options.ChunkOverlap)} must be non-negative and less than chunk length.");
        if (options.MaxChunks <= 0)
            return ValidateOptionsResult.Fail($"{nameof(options.MaxChunks)} must be positive.");
        if (options.MaxDownloadBytes <= 0)
            return ValidateOptionsResult.Fail($"{nameof(options.MaxDownloadBytes)} must be positive.");
        if (options.MaxImageBytes <= 0)
            return ValidateOptionsResult.Fail($"{nameof(options.MaxImageBytes)} must be positive.");
        return ValidateOptionsResult.Success;
    }
}
