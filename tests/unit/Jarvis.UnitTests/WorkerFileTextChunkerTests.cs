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

    [Fact]
    public void Split_reports_truncation_only_when_text_is_left_out()
    {
        var options = new FileProcessingOptions { ChunkLength = 100, ChunkOverlap = 10, MaxChunks = 2 };

        Assert.True(FileTextChunker.Split(new string('a', 500), options).Truncated);
        Assert.False(FileTextChunker.Split(new string('a', 150), options).Truncated);
        Assert.False(FileTextChunker.Split(new string('a', 100) + "     ",
            new FileProcessingOptions { ChunkLength = 100, ChunkOverlap = 10, MaxChunks = 1 }).Truncated);
    }

    [Fact]
    public void Split_prefers_a_paragraph_break_and_records_offsets_into_the_text()
    {
        var options = new FileProcessingOptions { ChunkLength = 100, ChunkOverlap = 0, MaxChunks = 10 };
        var first = "First paragraph " + new string('x', 54) + ".";
        var text = first + "\n\n" + "Second paragraph that runs on " + new string('y', 80);

        var result = FileTextChunker.Split(text, options);

        Assert.Equal(first, result.Chunks[0].Content);
        Assert.All(result.Chunks, chunk =>
            Assert.Equal(chunk.Content, text[chunk.StartOffset..chunk.EndOffset]));
        Assert.StartsWith("Second paragraph", result.Chunks[1].Content);
    }

    [Fact]
    public void Split_assigns_the_page_of_the_first_character()
    {
        var options = new FileProcessingOptions { ChunkLength = 60, ChunkOverlap = 0, MaxChunks = 10 };
        var page1 = "Page one text " + new string('a', 40);
        var page2 = "Page two text " + new string('b', 40);
        var text = page1 + "\n\n" + page2;

        var chunks = FileTextChunker.Split(text, options, [0, page1.Length + 2]).Chunks;

        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(2, chunks[^1].PageNumber);
        Assert.StartsWith("Page two", chunks[^1].Content);
        Assert.All(FileTextChunker.Split(text, options).Chunks, chunk => Assert.Null(chunk.PageNumber));
    }

    [Fact]
    public void Split_carries_the_nearest_markdown_heading()
    {
        var options = new FileProcessingOptions { ChunkLength = 80, ChunkOverlap = 0, MaxChunks = 10 };
        var text = "Intro without a heading " + new string('i', 40) + "\n\n# Lease terms\n\nThe tenant may end the " +
                   "lease with one month notice.\n\n## Deposit ##\n\nThe deposit is two months of rent.";

        var chunks = FileTextChunker.Split(text, options).Chunks;

        Assert.Null(chunks[0].Heading);
        Assert.Contains(chunks, chunk => chunk.Heading == "Lease terms" && chunk.Content.Contains("one month notice"));
        Assert.Equal("Deposit", chunks[^1].Heading);
    }

    [Fact]
    public void Default_options_index_the_whole_extraction_limit()
    {
        var options = new FileProcessingOptions();
        var text = string.Join(' ', Enumerable.Repeat("word", options.MaxExtractedCharacters / 5));

        var result = FileTextChunker.Split(text, options);

        Assert.False(result.Truncated);
        Assert.All(result.Chunks, chunk => Assert.True(chunk.Content.Length <= options.ChunkLength));
    }
}
