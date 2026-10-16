using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Domain.Files;
using Jarvis.Infrastructure.Files;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class FileRetrievalPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public async Task Keyword_search_matches_a_full_question_that_shares_only_some_words()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var contents = new FileContentRepository(database);
        var lease = await CreateFileAsync(database, owner, "lease.pdf", "ready",
            "The tenant may terminate the lease with one month notice.",
            "Rent is due on the first day of each month.");
        await CreateFileAsync(database, owner, "recipe.txt", "ready", "Bake the bread for forty minutes.");

        // websearch_to_tsquery required every word, so this question found nothing before.
        var hits = await contents.SearchTextAsync(owner, "How can I terminate my lease early?", CancellationToken.None);

        var best = hits[0];
        Assert.Equal(lease, best.FileId);
        Assert.Equal(0, best.ChunkIndex);
        Assert.InRange(best.Score, 0.01, 1.01);
        Assert.DoesNotContain(hits, hit => hit.FileName == "recipe.txt");
    }

    [Fact]
    public async Task Keyword_search_uses_prefixes_headings_typos_and_the_dutch_english_lexicon()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var contents = new FileContentRepository(database);
        var file = Guid.CreateVersion7();
        await new FileRepository(database).CreateAsync(StoredFile(file, owner, "handbook.md", "ready"), CancellationToken.None);
        await contents.ReplaceChunksAsync(file, owner,
        [
            new FileContentChunk(file, owner, 0, "Employees get twenty-five days per year.", Heading: "Vacation policy"),
            new FileContentChunk(file, owner, 1, "Trainings are booked through the portal."),
            new FileContentChunk(file, owner, 2, "Salarisgesprekken vinden in november plaats.")
        ], CancellationToken.None);

        Assert.Equal(0, (await contents.SearchTextAsync(owner, "vacation", CancellationToken.None))[0].ChunkIndex);
        Assert.Equal(0, (await contents.SearchTextAsync(owner, "Hoeveel vakantie krijg ik?", CancellationToken.None))[0].ChunkIndex);
        Assert.Equal(1, (await contents.SearchTextAsync(owner, "training", CancellationToken.None))[0].ChunkIndex);
        Assert.Equal(1, (await contents.SearchTextAsync(owner, "trainnings", CancellationToken.None))[0].ChunkIndex);
        Assert.Empty(await contents.SearchTextAsync(owner, "the of and", CancellationToken.None));
    }

    [Fact]
    public async Task Keyword_search_respects_owner_ready_status_and_scope()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var contents = new FileContentRepository(database);
        var ready = await CreateFileAsync(database, owner, "a.txt", "ready", "invoice renewal terms");
        var other = await CreateFileAsync(database, owner, "b.txt", "ready", "invoice payment terms");
        await CreateFileAsync(database, owner, "c.txt", "queued", "invoice draft");
        await CreateFileAsync(database, Guid.CreateVersion7(), "d.txt", "ready", "invoice of someone else");

        var all = await contents.SearchTextAsync(owner, "invoice", CancellationToken.None);
        Assert.Equal([ready, other], all.Select(hit => hit.FileId).Order());
        var scoped = await contents.SearchTextAsync(owner, "invoice", CancellationToken.None, [other]);
        Assert.Equal(other, Assert.Single(scoped).FileId);
        Assert.Empty(await contents.SearchTextAsync(owner, "invoice", CancellationToken.None, []));
    }

    [Fact]
    public async Task Semantic_search_compares_only_vectors_of_the_same_model_within_scope()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var contents = new FileContentRepository(database);
        var file = await CreateFileAsync(database, owner, "lease.pdf", "ready", "notice period", "deposit", "pets");
        var foreign = await CreateFileAsync(database, Guid.CreateVersion7(), "x.pdf", "ready", "notice period");

        Assert.False(await contents.HasEmbeddedChunksAsync(owner, "m1", null, CancellationToken.None));
        var pending = await contents.ListChunksNeedingEmbeddingAsync(owner, "m1", 10, CancellationToken.None);
        Assert.Equal(3, pending.Count);
        Assert.All(pending, chunk => Assert.Equal("lease.pdf", chunk.FileName));
        Assert.Contains(owner, await contents.ListOwnersNeedingEmbeddingAsync(100, CancellationToken.None));

        var byIndex = pending.OrderBy(chunk => chunk.Content).ToArray(); // deposit, notice period, pets
        await contents.SetChunkEmbeddingAsync(byIndex[1].ChunkId, owner, Vector("m1", 1, 0), CancellationToken.None);
        await contents.SetChunkEmbeddingAsync(byIndex[0].ChunkId, owner, Vector("m1", 0.6f, 0.8f), CancellationToken.None);
        await contents.SetChunkEmbeddingAsync(byIndex[2].ChunkId, owner, Vector("m2", 1, 0), CancellationToken.None);

        Assert.True(await contents.HasEmbeddedChunksAsync(owner, "m1", [file], CancellationToken.None));
        Assert.False(await contents.HasEmbeddedChunksAsync(owner, "m1", [foreign], CancellationToken.None));
        var hits = await contents.SearchSemanticAsync(owner, Vector("m1", 1, 0), null, 0.3, 10, CancellationToken.None);
        Assert.Equal(["notice period", "deposit"], hits.Select(hit => hit.Content));
        Assert.Equal(1, hits[0].Score, 3);
        Assert.Empty(await contents.SearchSemanticAsync(owner, Vector("m1", 1, 0), [foreign], 0.3, 10,
            CancellationToken.None));

        // The chunk embedded with the other model is queued again for m1.
        var stale = Assert.Single(await contents.ListChunksNeedingEmbeddingAsync(owner, "m1", 10, CancellationToken.None));
        Assert.Equal("pets", stale.Content);
    }

    [Fact]
    public async Task Hybrid_search_service_finds_a_passage_through_its_embedding_alone()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var contents = new FileContentRepository(database);
        await CreateFileAsync(database, owner, "lease.pdf", "ready",
            "The tenant may end the agreement with one month notice.", "Rent is due on the first day.");
        foreach (var chunk in await contents.ListChunksNeedingEmbeddingAsync(owner, "m1", 10, CancellationToken.None))
            await contents.SetChunkEmbeddingAsync(chunk.ChunkId, owner,
                chunk.Content.Contains("notice") ? Vector("m1", 1, 0) : Vector("m1", 0, 1), CancellationToken.None);

        var service = new FileSearchService(contents, new FixedEmbedder(Vector("m1", 0.95f, 0.1f)));
        var hits = await service.SearchAsync(owner, "cancel contract", CancellationToken.None);

        Assert.Contains("one month notice", hits[0].Content);
    }

    [Fact]
    public async Task Chunks_keep_page_and_heading_and_files_record_partial_indexing()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var files = new FileRepository(database);
        var contents = new FileContentRepository(database);
        var file = Guid.CreateVersion7();
        await files.CreateAsync(StoredFile(file, owner, "report.pdf", "ready"), CancellationToken.None);
        await contents.ReplaceChunksAsync(file, owner,
            [new FileContentChunk(file, owner, 0, "Quarterly revenue grew.", 10, 33, 4, "Results")], CancellationToken.None);
        await files.SetPartiallyIndexedAsync(file, owner, true, CancellationToken.None);

        var hit = Assert.Single(await contents.SearchTextAsync(owner, "revenue", CancellationToken.None));
        Assert.Equal(4, hit.PageNumber);
        Assert.Equal("Results",
            Assert.Single(await contents.ListChunksNeedingEmbeddingAsync(owner, "m", 5, CancellationToken.None)).Heading);
        Assert.True((await files.GetAsync(file, owner, CancellationToken.None))!.PartiallyIndexed);
    }

    private static async Task<Guid> CreateFileAsync(JarvisDbContext database, Guid owner, string name, string status,
        params string[] chunks)
    {
        var id = Guid.CreateVersion7();
        await new FileRepository(database).CreateAsync(StoredFile(id, owner, name, status), CancellationToken.None);
        await new FileContentRepository(database).ReplaceChunksAsync(id, owner,
            chunks.Select((content, index) => new FileContentChunk(id, owner, index, content)).ToArray(),
            CancellationToken.None);
        return id;
    }

    private static StoredFile StoredFile(Guid id, Guid owner, string name, string status) =>
        new(id, owner, id.ToString(), name, "text/plain", 32, new string('0', 64), DateTimeOffset.UtcNow, status);

    private static MemoryEmbedding Vector(string model, float x, float y) =>
        new(model, new[] { x, y }.Concat(Enumerable.Repeat(0f, 1534)).ToArray());

    private sealed class FixedEmbedder(MemoryEmbedding embedding) : IMemoryEmbedder, IResolvedEmbedder
    {
        public string Model => embedding.Model;

        public Task<IReadOnlyList<MemoryEmbedding>?> EmbedAsync(Guid ownerId, IReadOnlyList<string> texts,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryEmbedding>?>(texts.Select(_ => embedding).ToArray());

        public Task<IResolvedEmbedder?> ResolveAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IResolvedEmbedder?>(this);

        public Task<IReadOnlyList<MemoryEmbedding>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryEmbedding>>(texts.Select(_ => embedding).ToArray());
    }

    private JarvisDbContext CreateDbContext() => new(new DbContextOptionsBuilder<JarvisDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);
}
