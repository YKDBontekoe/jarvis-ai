using Microsoft.Extensions.Options;

namespace Jarvis.Worker.Files;

public sealed class FileProcessingOptions
{
    public const string SectionName = "FileProcessing";

    public int MaxExtractedCharacters { get; set; } = 300_000;

    public int ChunkLength { get; set; } = 3_200;

    public int ChunkOverlap { get; set; } = 320;

    public int MaxChunks { get; set; } = 120;

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
