using Jarvis.Agents;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class FileRetrievalTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");

    [Fact]
    public void Ranking_fuses_keyword_and_semantic_hits_and_rewards_agreement()
    {
        var both = Hit("lease.pdf", 0, 0.6);
        var keywordOnly = Hit("lease.pdf", 1, 0.6);
        var semanticOnly = Hit("notes.txt", 0, 0);

        var ranked = FileRanking.Rank([both, keywordOnly],
            [both with { Score = 0.8 }, semanticOnly with { Score = 0.75 }, keywordOnly with { Score = 0.35 }]);

        Assert.Equal(both.ChunkId, ranked[0].ChunkId);
        Assert.Contains(ranked, hit => hit.ChunkId == semanticOnly.ChunkId);
        Assert.All(ranked, hit => Assert.InRange(hit.Score, 0, 1));
    }

    [Fact]
    public void Ranking_drops_the_weak_tail_relative_to_the_best_hit()
    {
        var strong = Hit("a.txt", 0, 1);
        var weak = Hit("a.txt", 1, 0.2);

        var ranked = FileRanking.Rank([strong, weak], []);

        Assert.Equal(strong.ChunkId, Assert.Single(ranked).ChunkId);
        Assert.Empty(FileRanking.Rank([Hit("a.txt", 0, 0.01)], []));
    }

    [Fact]
    public void Ranking_keeps_at_most_max_hits()
    {
        var hits = Enumerable.Range(0, 20).Select(index => Hit("a.txt", index, 0.9)).ToArray();

        Assert.Equal(FileRanking.MaxHits, FileRanking.Rank(hits, []).Count);
        Assert.Equal(3, FileRanking.Rank(hits, [], 3).Count);
    }

    [Fact]
    public void Chunk_index_text_puts_document_page_and_section_in_front()
    {
        var text = FileChunkText.ForIndex(new FileChunkForIndex(Guid.NewGuid(), Guid.NewGuid(), "Huurcontract.pdf",
            "Opzegging", 3, "De huurder kan opzeggen met een maand."));

        Assert.Equal("Document: Huurcontract.pdf, page 3\nSection: Opzegging\n\nDe huurder kan opzeggen met een maand.",
            text);
        Assert.Equal("Document: a.txt\n\nbody",
            FileChunkText.ForIndex(new FileChunkForIndex(Guid.NewGuid(), Guid.NewGuid(), "a.txt", null, null, "body")));
    }

    [Fact]
    public async Task Search_uses_semantic_hits_when_the_scope_has_embedded_chunks()
    {
        var keyword = Hit("lease.pdf", 0, 0.7);
        var semanticOnly = Hit("lease.pdf", 4, 0);
        string? searchedModel = null;
        var repository = Fake<IFileContentRepository>.Create(
            ("SearchTextAsync", _ => (IReadOnlyList<FileSearchHit>)[keyword]),
            ("HasEmbeddedChunksAsync", args => (string)args[1]! == "test"),
            ("SearchSemanticAsync", args =>
            {
                searchedModel = ((MemoryEmbedding)args[1]!).Model;
                return (IReadOnlyList<FileSearchHit>)[semanticOnly with { Score = 0.8 }];
            }));

        var hits = await new FileSearchService(repository, new TestEmbedder())
            .SearchAsync(Owner, "how do I end the lease", default);

        Assert.Equal("test", searchedModel);
        Assert.Contains(hits, hit => hit.ChunkId == keyword.ChunkId);
        Assert.Contains(hits, hit => hit.ChunkId == semanticOnly.ChunkId);
    }

    [Fact]
    public async Task Search_skips_the_paid_embedding_when_nothing_in_scope_is_embedded()
    {
        var embedder = new TestEmbedder();
        var repository = Fake<IFileContentRepository>.Create(
            ("SearchTextAsync", _ => (IReadOnlyList<FileSearchHit>)[Hit("a.txt", 0, 0.9)]),
            ("HasEmbeddedChunksAsync", _ => false));

        var hits = await new FileSearchService(repository, embedder).SearchAsync(Owner, "lease", default);

        Assert.Single(hits);
        Assert.Equal(0, embedder.Calls);
    }

    [Fact]
    public async Task Search_falls_back_to_keywords_when_the_embedding_provider_fails()
    {
        var keyword = Hit("a.txt", 0, 0.9);
        var repository = Fake<IFileContentRepository>.Create(
            ("SearchTextAsync", _ => (IReadOnlyList<FileSearchHit>)[keyword]),
            ("HasEmbeddedChunksAsync", _ => true));

        var hits = await new FileSearchService(repository, new TestEmbedder(fail: true)).SearchAsync(Owner, "lease", default);

        Assert.Equal(keyword.ChunkId, Assert.Single(hits).ChunkId);
    }

    [Fact]
    public async Task Search_returns_nothing_for_an_empty_scope_without_querying()
    {
        var repository = Fake<IFileContentRepository>.Create();

        Assert.Empty(await new FileSearchService(repository).SearchAsync(Owner, "lease", default, []));
        Assert.Empty(await new FileSearchService(repository).SearchAsync(Owner, "   ", default));
    }

    [Fact]
    public void Reranker_selection_keeps_known_ids_in_model_order_and_falls_back()
    {
        var hits = Enumerable.Range(0, 8).Select(index => Hit("a.txt", index, 0.5)).ToArray();

        var chosen = FileReranker.Select(hits,
            $"[\"{hits[5].ChunkId}\", \"{Guid.NewGuid()}\", \"{hits[1].ChunkId}\", \"{hits[5].ChunkId}\"]");
        Assert.Equal([hits[5].ChunkId, hits[1].ChunkId], chosen.Select(hit => hit.ChunkId));

        Assert.Equal(5, FileReranker.Select(hits, "not json").Count);
        Assert.Equal(hits[0].ChunkId, FileReranker.Select(hits, "[]")[0].ChunkId);
    }

    [Fact]
    public async Task Indexer_embeds_pending_chunks_with_context_and_retries_one_by_one()
    {
        var chunks = Enumerable.Range(0, 3)
            .Select(index => new FileChunkForIndex(Guid.NewGuid(), Guid.NewGuid(), "plan.md", "Budget", null, "chunk " + index))
            .ToArray();
        var stored = new List<(Guid Chunk, string Model)>();
        var listed = 0;
        var repository = Fake<IFileContentRepository>.Create(
            ("ListChunksNeedingEmbeddingAsync", _ => listed++ == 0 ? chunks : (IReadOnlyList<FileChunkForIndex>)[]),
            ("SetChunkEmbeddingAsync", args =>
            {
                stored.Add(((Guid)args[0]!, ((MemoryEmbedding)args[2]!).Model));
                return Task.CompletedTask;
            }));
        var embedder = new TestEmbedder(failBatches: true);

        var embedded = await new FileChunkIndexer(repository, embedder, NullLogger<FileChunkIndexer>.Instance)
            .IndexOwnerAsync(Owner, default);

        Assert.Equal(3, embedded);
        Assert.Equal(chunks.Select(chunk => chunk.ChunkId), stored.Select(item => item.Chunk));
        Assert.All(stored, item => Assert.Equal("test", item.Model));
        Assert.Contains("Document: plan.md\nSection: Budget\n\nchunk 0", embedder.Texts);
    }

    [Fact]
    public async Task Indexer_does_nothing_without_an_embedding_model()
    {
        var repository = Fake<IFileContentRepository>.Create();

        Assert.Equal(0, await new FileChunkIndexer(repository, new TestEmbedder(noModel: true),
            NullLogger<FileChunkIndexer>.Instance).IndexOwnerAsync(Owner, default));
    }

    private static FileSearchHit Hit(string file, int index, double score)
    {
        var content = $"{file} passage {index}";
        return new FileSearchHit(Guid.NewGuid(), file, Guid.NewGuid(), index, content, score, 0, content.Length, null,
            content);
    }

    private sealed class TestEmbedder(bool fail = false, bool failBatches = false, bool noModel = false)
        : IMemoryEmbedder, IResolvedEmbedder
    {
        public int Calls { get; private set; }
        public List<string> Texts { get; } = [];
        public string Model => "test";

        public async Task<IReadOnlyList<MemoryEmbedding>?> EmbedAsync(Guid ownerId, IReadOnlyList<string> texts,
            CancellationToken cancellationToken) => await EmbedAsync(texts, cancellationToken);

        public Task<IResolvedEmbedder?> ResolveAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IResolvedEmbedder?>(noModel ? null : this);

        public Task<IReadOnlyList<MemoryEmbedding>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (fail || (failBatches && texts.Count > 1))
                return Task.FromException<IReadOnlyList<MemoryEmbedding>>(new HttpRequestException("provider down"));
            Texts.AddRange(texts);
            return Task.FromResult<IReadOnlyList<MemoryEmbedding>>(
                texts.Select(_ => new MemoryEmbedding(Model, [1f, 0f])).ToArray());
        }
    }
}
