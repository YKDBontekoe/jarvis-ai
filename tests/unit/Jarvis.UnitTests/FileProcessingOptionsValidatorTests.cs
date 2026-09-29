using Jarvis.Worker.Files;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class FileProcessingOptionsValidatorTests
{
    private readonly FileProcessingOptionsValidator _validator = new();

    [Fact]
    public void Default_options_validate()
    {
        var result = _validator.Validate(null, new FileProcessingOptions());
        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Rejects_overlap_not_less_than_chunk_length()
    {
        var result = _validator.Validate(null, new FileProcessingOptions { ChunkLength = 100, ChunkOverlap = 100 });
        Assert.True(result.Failed);
    }
}
