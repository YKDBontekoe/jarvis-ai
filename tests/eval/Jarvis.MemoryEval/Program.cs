using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jarvis.Application.Memory;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Memory;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

// Offline retrieval eval for the memory bank. Seeds mock memories into a scratch PostgreSQL database and scores
// IMemoryService.SearchAsync against graded relevance labels. Usage:
//   MEMORY_EVAL_DB="Host=localhost;Database=jarvis_eval;Username=jarvis;Password=..." dotnet run -- [label]
var connectionString = Environment.GetEnvironmentVariable("MEMORY_EVAL_DB")
    ?? throw new InvalidOperationException("Set MEMORY_EVAL_DB to a scratch PostgreSQL connection string.");
var label = args.FirstOrDefault() ?? "run";
// "--held-out" scores queries written before any tuning and never used to tune. "--updates" scores corrections
// and restatements: memory extraction can only dedupe or supersede a memory that search returns for the new message.
var heldOut = args.Contains("--held-out");
var updates = args.Contains("--updates");
// "--scale N" adds N filler memories built from the dataset's own vocabulary to test speed and ranking under load.
// "--embeddings file.json" (from embed_dataset.py) turns on the semantic path: memories are indexed with those vectors
// through IMemoryIndexRepository, queries are embedded by a lookup embedder. "--min-sim x" overrides the similarity floor.
var embeddingsIndex = Array.IndexOf(args, "--embeddings");
var lookup = embeddingsIndex >= 0 ? EmbeddingLookup.Load(args[embeddingsIndex + 1]) : null;
// "--agent-queries file.json" simulates agentic search: instead of the raw user message, the model's own 1-3 search
// queries per message (one SearchMemory call each) are run and the hits merged by best score, as the agent would see them.
var agentIndex = Array.IndexOf(args, "--agent-queries");
var agentQueries = agentIndex >= 0
    ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(args[agentIndex + 1]))
    : null;
var simIndex = Array.IndexOf(args, "--min-sim");
if (simIndex >= 0) MemoryService.MinimumSemanticSimilarityOverride = double.Parse(args[simIndex + 1], CultureInfo.InvariantCulture);
var scaleIndex = Array.IndexOf(args, "--scale");
var fillers = scaleIndex >= 0 && scaleIndex + 1 < args.Length ? int.Parse(args[scaleIndex + 1], CultureInfo.InvariantCulture) : 0;
const int Runs = 7;

// "--dataset file.json" swaps the built-in mock set (e.g. for the 1,000-memory set from merge_large.py).
// "--pool N --dump file.json" widens the candidate pool and writes each query's candidates (key, content, score) for a
// rerank experiment; "--rerank file.json" then scores the order/subset a model chose per query text.
var poolIndex = Array.IndexOf(args, "--pool");
var pool = poolIndex >= 0 ? int.Parse(args[poolIndex + 1]) : Jarvis.Application.Memory.MemoryRanking.MaxHits;
// "--hints file.json" ({key: hints}) stores model-written search hints (IMemoryIndexRepository.SetSearchHintsAsync) and
// embeds content + hints, like the background indexer does.
var hintsIndex = Array.IndexOf(args, "--hints");
var hintsByKey = hintsIndex >= 0
    ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(args[hintsIndex + 1]))!
    : new Dictionary<string, string>();
var dumpIndex = Array.IndexOf(args, "--dump");
var dump = new Dictionary<string, object>();
var rerankIndex = Array.IndexOf(args, "--rerank");
var reranked = rerankIndex >= 0
    ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(await File.ReadAllTextAsync(args[rerankIndex + 1]))
    : null;
// "--prior file.json" ({message: previous message}) searches each message alone and with its previous message, merged
// with MemoryRanking.MergeWithContext at weight "--prior-weight w" (default 0.6).
var priorIndex = Array.IndexOf(args, "--prior");
var priors = priorIndex >= 0
    ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(args[priorIndex + 1]))
    : null;
var priorWeightIndex = Array.IndexOf(args, "--prior-weight");
var priorWeight = priorWeightIndex >= 0 ? double.Parse(args[priorWeightIndex + 1], CultureInfo.InvariantCulture) : 0.6;
var datasetIndex = Array.IndexOf(args, "--dataset");
var datasetPath = datasetIndex >= 0 ? args[datasetIndex + 1] : Path.Combine(AppContext.BaseDirectory, "dataset.json");
var dataset = JsonSerializer.Deserialize<Dataset>(
    await File.ReadAllTextAsync(datasetPath),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

JarvisDbContext CreateDb() => new(new DbContextOptionsBuilder<JarvisDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.UseVector()).Options);

await using (var setup = CreateDb())
{
    await setup.Database.MigrateAsync();
    await setup.Database.ExecuteSqlRawAsync("DELETE FROM memories");
}

var owner = Guid.CreateVersion7();
var otherOwner = Guid.CreateVersion7();
var idsByKey = new Dictionary<string, Guid>();
var keysById = new Dictionary<Guid, string>();
var foreignIds = new HashSet<Guid>();
await using (var seed = CreateDb())
{
    var service = new MemoryService(new MemoryRepository(seed));
    foreach (var memory in dataset.Memories)
    {
        var record = await service.CreateAsync(owner, memory.Kind, memory.Content, memory.Importance, 0.9f, null,
            memory.Pinned, CancellationToken.None, sourceType: memory.SourceType ?? "conversation");
        hintsByKey.TryGetValue(memory.Key, out var hints);
        if (hints is not null)
            await new MemoryIndexRepository(seed).SetSearchHintsAsync(record.Id, owner, hints, CancellationToken.None);
        if (lookup is not null)
            await new MemoryIndexRepository(seed).SetEmbeddingAsync(record.Id, owner,
                lookup.Embedding(hints is null ? memory.Content : memory.Content + " " + hints), CancellationToken.None);
        idsByKey[memory.Key] = record.Id;
        keysById[record.Id] = memory.Key;
        var at = DateTimeOffset.UtcNow.AddDays(-memory.AgeDays);
        await seed.Memories.Where(x => x.Id == record.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.CreatedAt, at).SetProperty(x => x.UpdatedAt, at)
            .SetProperty(x => x.ValidUntil, memory.Expired ? at.AddDays(1) : null));
    }
    var vocabulary = dataset.Memories.SelectMany(memory => memory.Content.Split(' ')).Distinct().ToArray();
    var random = new Random(42);
    for (var filler = 0; filler < fillers; filler++)
        await service.CreateAsync(owner, "other", string.Join(' ',
                Enumerable.Range(0, 8 + random.Next(10)).Select(_ => vocabulary[random.Next(vocabulary.Length)])),
            0.3f, 0.9f, null, false, CancellationToken.None);
    foreach (var content in dataset.OtherOwnerMemories)
    {
        var foreign = await service.CreateAsync(otherOwner, "fact", content, 0.9f, 0.9f, null, false,
            CancellationToken.None);
        foreignIds.Add(foreign.Id);
        if (lookup is not null)
            await new MemoryIndexRepository(seed).SetEmbeddingAsync(foreign.Id, otherOwner, lookup.Embedding(content),
                CancellationToken.None);
    }
}

// Autovacuum keeps planner statistics fresh in a running system; do the same so plans are realistic.
await using (var analyze = CreateDb())
    await analyze.Database.ExecuteSqlRawAsync("ANALYZE memories");

var expiredIds = dataset.Memories.Where(x => x.Expired).Select(x => idsByKey[x.Key]).ToHashSet();
var results = new List<QueryResult>();
var latencies = new List<double>();
foreach (var query in updates ? dataset.UpdateStatements ?? [] : heldOut ? dataset.HeldOutQueries ?? [] : dataset.Queries)
{
    IReadOnlyList<Guid> ranked = [];
    IReadOnlyList<double> scores = [];
    var queryLatencies = new List<double>();
    for (var run = 0; run < Runs; run++)
    {
        // A fresh context per call mirrors a scoped request in the API.
        await using var db = CreateDb();
        var service = lookup is null
            ? new MemoryService(new MemoryRepository(db))
            : new MemoryService(new MemoryRepository(db), new MemoryIndexRepository(db), lookup);
        var started = Stopwatch.GetTimestamp();
        IReadOnlyList<Jarvis.Domain.Memory.MemorySearchHit> hits;
        if (priors is not null)
        {
            var alone = await service.SearchAsync(owner, query.Text, CancellationToken.None, maxHits: pool);
            var withContext = await service.SearchAsync(owner, priors[query.Text] + " " + query.Text,
                CancellationToken.None, maxHits: pool);
            hits = Jarvis.Application.Memory.MemoryRanking.MergeWithContext(alone, withContext, priorWeight, pool);
        }
        else if (agentQueries is null) hits = await service.SearchAsync(owner, query.Text, CancellationToken.None, maxHits: pool);
        else
        {
            var merged = new Dictionary<Guid, Jarvis.Domain.Memory.MemorySearchHit>();
            foreach (var agentQuery in agentQueries[query.Text])
                foreach (var hit in await service.SearchAsync(owner, agentQuery, CancellationToken.None, maxHits: pool))
                    if (!merged.TryGetValue(hit.Memory.Id, out var seen) || hit.Score > seen.Score) merged[hit.Memory.Id] = hit;
            hits = merged.Values.OrderByDescending(hit => hit.Score).Take(pool).ToArray();
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (run > 0) // run 0 warms the connection pool and query plans
        {
            latencies.Add(elapsed);
            queryLatencies.Add(elapsed);
        }
        if (run == 0 && dumpIndex >= 0)
            dump[query.Text] = hits.Select(hit => new { key = idsByKey.First(x => x.Value == hit.Memory.Id).Key, content = dataset.Memories.First(m => idsByKey[m.Key] == hit.Memory.Id).Content, score = hit.Score }).ToArray();
        if (reranked is not null)
            hits = reranked[query.Text].Where(idsByKey.ContainsKey).Select(key => hits.FirstOrDefault(hit => hit.Memory.Id == idsByKey[key])).Where(hit => hit is not null).ToArray()!;
        ranked = hits.Select(hit => hit.Memory.Id).ToArray();
        scores = hits.Select(hit => hit.Score).ToArray();
    }
    results.Add(Score(query, ranked) with
    {
        MedianMs = queryLatencies.Order().ElementAt(queryLatencies.Count / 2),
        // Before this change every chat turn with three or more hits waited on a model rerank (up to 8 s). Now chat
        // turns never do, and the SearchMemory tool only does when the top hit does not clearly win (MemoryReranker).
        RerankEligible = scores.Count >= 3,
        RerankAmbiguous = scores.Count >= 3 && scores[0] < 1.5 * scores[1]
    });
}

if (dumpIndex >= 0) await File.WriteAllTextAsync(args[dumpIndex + 1], JsonSerializer.Serialize(dump));

double Mean(Func<QueryResult, double> selector) => results.Average(selector);
latencies.Sort();
double Percentile(double p) => latencies[(int)Math.Clamp(Math.Ceiling(p * latencies.Count) - 1, 0, latencies.Count - 1)];
var summary = new
{
    label,
    queries = results.Count,
    recallAt3 = Mean(r => r.RecallAt3),
    recallAt8 = Mean(r => r.RecallAt8),
    primaryHitAt1 = Mean(r => r.PrimaryAt1),
    mrr = Mean(r => r.Mrr),
    ndcgAt8 = Mean(r => r.NdcgAt8),
    noiseShare = results.Sum(r => r.Irrelevant) / (double)Math.Max(1, results.Sum(r => r.Returned)),
    avgReturned = Mean(r => r.Returned),
    emptyQueries = results.Count(r => r.Returned == 0),
    expiredLeaks = results.Sum(r => r.ExpiredLeaks),
    otherOwnerLeaks = results.Sum(r => r.ForeignLeaks),
    turnsThatWaitedOnRerankBefore = results.Count(r => r.RerankEligible),
    toolSearchesThatStillRerank = results.Count(r => r.RerankAmbiguous),
    p50Ms = Percentile(0.50),
    p95Ms = Percentile(0.95)
};
var summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(summaryJson);
var reportIndex = Array.IndexOf(args, "--report");
if (reportIndex >= 0)
{
    var report = Path.GetFullPath(args[reportIndex + 1]);
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    await File.WriteAllTextAsync(report, summaryJson + "\n");
}
// Isolation is an invariant, even when no quality baseline was requested.
if (summary.expiredLeaks != 0 || summary.otherOwnerLeaks != 0) Environment.ExitCode = 1;
var thresholdsIndex = Array.IndexOf(args, "--thresholds");
if (thresholdsIndex >= 0)
{
    using var thresholds = JsonDocument.Parse(await File.ReadAllTextAsync(args[thresholdsIndex + 1]));
    using var metrics = JsonDocument.Parse(summaryJson);
    foreach (var threshold in thresholds.RootElement.EnumerateObject())
    {
        var value = metrics.RootElement.GetProperty(threshold.Name).GetDouble();
        if (!double.IsFinite(value) ||
            (threshold.Value.TryGetProperty("minimum", out var minimum) && value < minimum.GetDouble()) ||
            (threshold.Value.TryGetProperty("maximum", out var maximum) && value > maximum.GetDouble()))
        {
            Console.Error.WriteLine($"Memory evaluation failed threshold: {threshold.Name}.");
            Environment.ExitCode = 1;
        }
    }
}
foreach (var result in results)
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{result.RecallAt8:0.00} {result.Mrr:0.00} {result.MedianMs,6:0.0}ms [{string.Join(", ", result.TopKeys)}]  {result.Query}"));

QueryResult Score(Query query, IReadOnlyList<Guid> ranked)
{
    int Grade(Guid id) => keysById.TryGetValue(id, out var key) ? query.Relevant.GetValueOrDefault(key) : 0;
    var relevant = query.Relevant.Count;
    var foundAt = (int k) => ranked.Take(k).Count(id => Grade(id) > 0) / (double)relevant;
    var firstPrimary = ranked.Select((id, index) => (id, index)).FirstOrDefault(x => Grade(x.id) == 2);
    var mrr = Grade(firstPrimary.id) == 2 ? 1d / (firstPrimary.index + 1) : 0;
    double Dcg(IEnumerable<int> grades) => grades.Select((g, i) => (Math.Pow(2, g) - 1) / Math.Log2(i + 2)).Sum();
    var ideal = Dcg(query.Relevant.Values.OrderByDescending(g => g).Take(8));
    return new QueryResult(query.Text, foundAt(3), foundAt(8),
        ranked.Count > 0 && Grade(ranked[0]) == 2 ? 1 : 0, mrr, Dcg(ranked.Take(8).Select(Grade)) / ideal,
        ranked.Count, ranked.Count(id => Grade(id) == 0), ranked.Count(expiredIds.Contains),
        ranked.Count(foreignIds.Contains),
        ranked.Take(5).Select(id => keysById.GetValueOrDefault(id, "?")).ToArray());
}

internal sealed record Dataset(List<SeedMemory> Memories, List<string> OtherOwnerMemories, List<Query> Queries,
    List<Query>? HeldOutQueries = null, List<Query>? UpdateStatements = null);
internal sealed record SeedMemory(string Key, string Kind, string Content, double AgeDays, float Importance,
    bool Pinned = false, bool Expired = false, string? SourceType = null);
internal sealed record Query(string Text, Dictionary<string, int> Relevant);
internal sealed record QueryResult(string Query, double RecallAt3, double RecallAt8, double PrimaryAt1, double Mrr,
    double NdcgAt8, int Returned, int Irrelevant, int ExpiredLeaks, int ForeignLeaks, string[] TopKeys,
    double MedianMs = 0, bool RerankEligible = false, bool RerankAmbiguous = false);

/// <summary>Serves precomputed vectors (zero-padded to the 1536 columns) instead of calling a provider.</summary>
internal sealed class EmbeddingLookup(string model, Dictionary<string, float[]> vectors) : IMemoryEmbedder
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

    public MemoryEmbedding Embedding(string text) => new(model, vectors[text]);

    public Task<IReadOnlyList<MemoryEmbedding>?> EmbedAsync(Guid ownerId, IReadOnlyList<string> texts,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemoryEmbedding>?>(texts.Select(Embedding).ToArray());
}
