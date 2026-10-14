using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Domain.Files;
using Jarvis.Infrastructure.Files;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Worker.Files;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

// Offline retrieval eval for file search. Chunks invented documents like the worker does, stores them for one owner
// (and a copy for a second owner to check isolation) in a scratch PostgreSQL database, and scores the ranked chunks
// against the passage that answers each query. Usage:
//   FILE_EVAL_DB="Host=localhost;Database=jarvis_eval;Username=jarvis;Password=..." dotnet run -- [before|after]
// "before" reproduces the previous pipeline: 3,200-character chunks without headings, and a websearch_to_tsquery
// query that requires every word, ranked by ts_rank. "after" runs FileSearchService on the current chunking.
// "--embeddings file.json" (from embed_texts.py) adds the semantic path with precomputed vectors; "--dump-texts
// file.json" writes the chunk and query texts to embed.
var connectionString = Environment.GetEnvironmentVariable("FILE_EVAL_DB")
    ?? throw new InvalidOperationException("Set FILE_EVAL_DB to a scratch PostgreSQL connection string.");
var label = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "after";
var before = label == "before";
var embeddingsIndex = Array.IndexOf(args, "--embeddings");
var lookup = embeddingsIndex >= 0 ? EmbeddingLookup.Load(args[embeddingsIndex + 1]) : null;
var dumpIndex = Array.IndexOf(args, "--dump-texts");
// "--scale n" adds n filler documents of about 8,000 characters built from the dataset's own words, to measure
// speed and ranking under load.
var scaleIndex = Array.IndexOf(args, "--scale");
var fillers = scaleIndex >= 0 ? int.Parse(args[scaleIndex + 1], CultureInfo.InvariantCulture) : 0;
const int Runs = 5;

var dataset = JsonSerializer.Deserialize<Dataset>(
    await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "dataset.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

JarvisDbContext CreateDb() => new(new DbContextOptionsBuilder<JarvisDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.UseVector()).Options);

await using (var setup = CreateDb())
{
    await setup.Database.MigrateAsync();
    await setup.Database.ExecuteSqlRawAsync("DELETE FROM files");
}

// "--chunk-length n" (overlap a tenth of it) separates the chunk size from the query change.
var chunkLengthIndex = Array.IndexOf(args, "--chunk-length");
var options = before
    ? new FileProcessingOptions { ChunkLength = 3_200, ChunkOverlap = 320, MaxChunks = 120 }
    : new FileProcessingOptions();
if (chunkLengthIndex >= 0)
{
    options.ChunkLength = int.Parse(args[chunkLengthIndex + 1], CultureInfo.InvariantCulture);
    options.ChunkOverlap = options.ChunkLength / 10;
}
var owner = Guid.CreateVersion7();
var otherOwner = Guid.CreateVersion7();
var keyByFile = new Dictionary<Guid, string>();
var chunkCount = 0;
await using (var db = CreateDb())
{
    var files = new FileRepository(db);
    var contents = new FileContentRepository(db);
    foreach (var seedOwner in new[] { owner, otherOwner })
    foreach (var document in dataset.Documents)
    {
        var id = Guid.CreateVersion7();
        keyByFile[id] = seedOwner == owner ? document.Key : "foreign:" + document.Key;
        var (text, pageStarts) = document.Pages is { } pages ? JoinPages(pages) : (document.Text!, null);
        var chunks = FileTextChunker.Split(text, options, before ? null : pageStarts).Chunks;
        await files.CreateAsync(new StoredFile(id, seedOwner, id.ToString(), document.FileName, "text/plain",
            text.Length, new string('0', 64), DateTimeOffset.UtcNow, "ready"), CancellationToken.None);
        await contents.ReplaceChunksAsync(id, seedOwner, chunks.Select((chunk, index) => new FileContentChunk(id,
            seedOwner, index, chunk.Content, chunk.StartOffset, chunk.EndOffset, chunk.PageNumber,
            before ? null : chunk.Heading)).ToArray(), CancellationToken.None);
        if (seedOwner == owner) chunkCount += chunks.Count;
    }

    var vocabulary = dataset.Documents
        .SelectMany(document => (document.Text ?? string.Join(' ', document.Pages!)).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries))
        .ToArray();
    var random = new Random(42);
    for (var index = 0; index < fillers; index++)
    {
        var id = Guid.CreateVersion7();
        keyByFile[id] = "filler";
        var text = string.Join(' ', Enumerable.Range(0, 1_300).Select(_ => vocabulary[random.Next(vocabulary.Length)]));
        var chunks = FileTextChunker.Split(text, options).Chunks;
        await files.CreateAsync(new StoredFile(id, owner, id + ".txt", $"filler-{index}.txt", "text/plain",
            text.Length, new string('0', 64), DateTimeOffset.UtcNow, "ready"), CancellationToken.None);
        await contents.ReplaceChunksAsync(id, owner, chunks.Select((chunk, chunkIndex) => new FileContentChunk(id,
            owner, chunkIndex, chunk.Content)).ToArray(), CancellationToken.None);
        chunkCount += chunks.Count;
    }
    if (fillers > 0)
        await db.Database.ExecuteSqlRawAsync("ANALYZE file_content_chunks; ANALYZE files");

    if (dumpIndex >= 0)
    {
        var texts = (await contents.ListChunksNeedingEmbeddingAsync(owner, "dump", 10_000, CancellationToken.None))
            .Select(FileChunkText.ForIndex).Concat(dataset.Queries.Select(query => query.Text)).Distinct().ToArray();
        await File.WriteAllTextAsync(args[dumpIndex + 1], JsonSerializer.Serialize(texts));
        Console.WriteLine($"Wrote {texts.Length} texts to {args[dumpIndex + 1]}.");
        return;
    }

    if (lookup is not null)
        foreach (var seedOwner in new[] { owner, otherOwner })
        foreach (var chunk in await contents.ListChunksNeedingEmbeddingAsync(seedOwner, lookup.Model, 10_000,
                     CancellationToken.None))
            await contents.SetChunkEmbeddingAsync(chunk.ChunkId, seedOwner, lookup.Embedding(FileChunkText.ForIndex(chunk)),
                CancellationToken.None);
}

var results = new List<QueryResult>();
foreach (var query in dataset.Queries)
{
    IReadOnlyList<FileSearchHit> hits = [];
    var timings = new List<double>();
    for (var run = 0; run <= Runs; run++)
    {
        await using var db = CreateDb();
        var started = Stopwatch.GetTimestamp();
        hits = before
            ? await SearchBeforeAsync(db, owner, query.Text)
            : await new FileSearchService(new FileContentRepository(db), lookup).SearchAsync(owner, query.Text,
                CancellationToken.None);
        if (run > 0) timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    var relevant = hits.Select(hit => keyByFile[hit.FileId] == query.Document &&
                                      query.Evidence.Any(evidence => Normalize(hit.Content).Contains(Normalize(evidence))))
        .ToArray();
    var first = Array.IndexOf(relevant, true);
    timings.Sort();
    results.Add(new QueryResult(query.Text,
        first is >= 0 and < 3 ? 1 : 0, first >= 0 ? 1 : 0, first == 0 ? 1 : 0, first >= 0 ? 1d / (first + 1) : 0,
        hits.Count, hits.Count(hit => keyByFile[hit.FileId].StartsWith("foreign:", StringComparison.Ordinal)),
        first >= 0 ? hits.Take(first + 1).Sum(hit => hit.Content.Length) : -1,
        timings[timings.Count / 2]));
}

var answered = results.Where(result => result.ContextCharacters >= 0).ToArray();
var latencies = results.Select(result => result.MedianMs).Order().ToArray();
Console.WriteLine($"File retrieval eval — {label}{(lookup is null ? "" : " + " + lookup.Model)}: " +
                  $"{dataset.Documents.Count} documents, {chunkCount} chunks, {results.Count} queries");
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"recall@3 {results.Average(r => r.RecallAt3):0.00}  recall@8 {results.Average(r => r.RecallAt8):0.00}  " +
    $"hit@1 {results.Average(r => r.HitAt1):0.00}  MRR {results.Average(r => r.Mrr):0.00}  " +
    $"empty {results.Count(r => r.Returned == 0)}  leaks {results.Sum(r => r.Leaks)}  " +
    $"context chars to answer {(answered.Length == 0 ? 0 : answered.Average(r => r.ContextCharacters)):0}  " +
    $"p50 {latencies[latencies.Length / 2]:0.0} ms  p95 {latencies[(int)(latencies.Length * 0.95)]:0.0} ms"));
if (args.Contains("--verbose"))
    foreach (var result in results.Where(result => result.HitAt1 == 0))
        Console.WriteLine($"  miss@1 ({(result.RecallAt8 > 0 ? "found lower" : "not found")}): {result.Query}");

static (string Text, IReadOnlyList<int> PageStarts) JoinPages(IReadOnlyList<string> pages)
{
    // The same layout StoredFileProcessor builds for PDFs: pages separated by a blank line.
    var text = new System.Text.StringBuilder();
    var starts = new List<int>();
    foreach (var page in pages)
    {
        if (text.Length > 0) text.Append("\n\n");
        starts.Add(text.Length);
        text.Append(page.TrimEnd());
    }
    return (text.ToString(), starts);
}

static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

// The search before this change: every word required (websearch_to_tsquery), ranked by ts_rank, top 8.
static async Task<IReadOnlyList<FileSearchHit>> SearchBeforeAsync(JarvisDbContext db, Guid owner, string query)
{
    var rows = await db.FileContentChunks.FromSqlInterpolated($"""
        SELECT c.* FROM file_content_chunks c
        JOIN files f ON f."Id" = c.file_id AND f.owner_id = c.owner_id
        WHERE c.owner_id = {owner}
          AND f.processing_status = 'ready'
          AND c.search_text @@ websearch_to_tsquery('simple', {query})
        ORDER BY ts_rank(c.search_text, websearch_to_tsquery('simple', {query})) DESC
        LIMIT 8
        """).AsNoTracking().ToListAsync();
    return rows.Select(row => new FileSearchHit(row.FileId, string.Empty, row.Id, row.ChunkIndex, row.Content, 0, 0,
        row.Content.Length, row.PageNumber, row.Content)).ToArray();
}

internal sealed record Dataset(List<Document> Documents, List<Query> Queries);
internal sealed record Document(string Key, string FileName, string? Text, List<string>? Pages);
internal sealed record Query(string Text, string Document, List<string> Evidence);
internal sealed record QueryResult(string Query, double RecallAt3, double RecallAt8, double HitAt1, double Mrr,
    int Returned, int Leaks, int ContextCharacters, double MedianMs);

/// <summary>Serves precomputed vectors (zero-padded to the 1536 columns) instead of calling a provider.</summary>
internal sealed class EmbeddingLookup(string model, Dictionary<string, float[]> vectors) : IMemoryEmbedder, IResolvedEmbedder
{
    public static EmbeddingLookup Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var model = document.RootElement.GetProperty("model").GetString()!;
        var vectors = document.RootElement.GetProperty("vectors").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(value => value.GetSingle())
                .Concat(Enumerable.Repeat(0f, 1536)).Take(1536).ToArray());
        return new EmbeddingLookup(model, vectors);
    }

    public string Model => model;

    public MemoryEmbedding Embedding(string text) => new(model, vectors[text]);

    public Task<IReadOnlyList<MemoryEmbedding>?> EmbedAsync(Guid ownerId, IReadOnlyList<string> texts,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemoryEmbedding>?>(texts.Select(Embedding).ToArray());

    public Task<IResolvedEmbedder?> ResolveAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IResolvedEmbedder?>(this);

    public Task<IReadOnlyList<MemoryEmbedding>> EmbedAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemoryEmbedding>>(texts.Select(Embedding).ToArray());
}
