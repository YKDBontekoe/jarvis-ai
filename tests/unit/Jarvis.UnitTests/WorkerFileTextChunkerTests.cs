using Jarvis.Worker.Files;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WorkerFileTextChunkerTests
{
    [Fact]
    public void SplitIntoChunks_respects_length_overlap_and_max_chunks()
    {
        var options = new FileProcessingOptions
        {
            ChunkLength = 100,
            ChunkOverlap = 10,
            MaxChunks = 3
        };
        var text = new string('a', 250) + " boundary " + new string('b', 250);

        var chunks = FileTextChunker.SplitIntoChunks(text, options);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 100));
    }

    [Fact]
    public void SplitIntoChunks_skips_empty_segments()
    {
        var options = new FileProcessingOptions { ChunkLength = 50, ChunkOverlap = 5, MaxChunks = 10 };
        var chunks = FileTextChunker.SplitIntoChunks("   \n\n   ", options);
        Assert.Empty(chunks);
    }
}
