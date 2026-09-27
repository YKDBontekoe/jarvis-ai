using Jarvis.Application.Files;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class FileIndexingTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Accepted_searchable_types_are_indexable(string contentType) =>
        Assert.True(FileIndexing.IsIndexable(contentType));

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("image/gif")]
    [InlineData("audio/mpeg")]
    public void Unsupported_types_are_not_indexable(string contentType) =>
        Assert.False(FileIndexing.IsIndexable(contentType));
}
