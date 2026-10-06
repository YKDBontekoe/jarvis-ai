using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jarvis.FileEval;

// Offline eval of the document (file) retrieval pipeline over a large library. Workflow (see README.md):
//   FILE_EVAL_HOST="Host=localhost;Username=jarvis;Password=..." (a scratch server; databases fe_* are created and dropped)
//   index        --lib DIR --tag TAG [--chunker fixed|recursive|recursive-ctx] [--length 3200] [--overlap 320] [--foreign 33]
//   prepare-lex  --lib DIR --tag TAG                     stemmed columns + document frequencies for the lexical variants
//   embed-load   --lib DIR --tag TAG --model NAME        loads emb-NAME.* (from embed.py) and rebuilds the HNSW index
//   run          --lib DIR --tag TAG --variant V --name N [--opt k=v ...] [--runs 1]   writes runs-TAG/N.jsonl
var command = args.FirstOrDefault() ?? "help";
if (command == "help")
{
    Console.WriteLine("Commands: index, prepare-lex, embed-load, run. See tests/eval/Jarvis.FileEval/README.md.");
    return;
}

var workspace = new Workspace(Args.Require(args, "--lib"), Args.Require(args, "--tag"));
switch (command)
{
    case "index":
        await Indexing.IndexAsync(workspace, Args.Get(args, "--chunker") ?? "fixed",
            int.Parse(Args.Get(args, "--length") ?? "3200"), int.Parse(Args.Get(args, "--overlap") ?? "320"),
            int.Parse(Args.Get(args, "--foreign") ?? "33"));
        break;
    case "prepare-lex":
        await Indexing.PrepareLexicalAsync(workspace);
        break;
    case "embed-load":
        await Indexing.LoadEmbeddingsAsync(workspace, Args.Require(args, "--model"));
        break;
    case "run":
        await RunAsync(workspace, args);
        break;
    default:
        throw new ArgumentException($"Unknown command {command}.");
}

static async Task RunAsync(Workspace workspace, string[] args)
{
    var variant = Args.Require(args, "--variant");
    var name = Args.Require(args, "--name");
    var options = Args.Options(args);
    var runs = int.Parse(Args.Get(args, "--runs") ?? "1");
    const int Limit = 30;
    var queries = workspace.Queries();
    var rowById = Json.ReadLines<ChunkRow>(workspace.ChunksPath).Select((row, index) => (row.Id, index))
        .ToDictionary(x => x.Id, x => x.index);
    await using var source = workspace.DataSource();
    var retriever = await Retrievers.CreateAsync(workspace, source, variant, options, queries);
    var scopes = options.TryGetValue("scope", out var scopeSize) ? Scopes.Build(workspace, queries, int.Parse(scopeSize)) : null;

    for (var warm = 0; warm < Math.Min(20, queries.Count); warm++) await retriever.SearchAsync(warm, queries[warm], Limit, scopes?[warm]);

    Directory.CreateDirectory(workspace.RunsDir);
    await using var output = new StreamWriter(Path.Combine(workspace.RunsDir, name + ".jsonl"), false, new UTF8Encoding(false));
    var latencies = new List<double>();
    var empty = 0;
    var leaks = 0;
    for (var index = 0; index < queries.Count; index++)
    {
        var timings = new List<double>();
        List<Hit> hits = [];
        for (var run = 0; run < runs; run++)
        {
            var started = Stopwatch.GetTimestamp();
            hits = await retriever.SearchAsync(index, queries[index], Limit, scopes?[index]);
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        var median = timings.Order().ElementAt(timings.Count / 2);
        var mapped = hits.Where(hit => rowById.ContainsKey(hit.Id)).Select(hit => new[] { rowById[hit.Id], hit.Score }).ToArray();
        var leaked = hits.Count - mapped.Length;
        leaks += leaked;
        if (hits.Count == 0) empty++;
        latencies.Add(median);
        await output.WriteLineAsync(JsonSerializer.Serialize(new RunLine(queries[index].Qid, median, leaked, mapped), Json.Web));
    }
    latencies.Sort();
    Console.WriteLine($"{name}: {queries.Count} queries, {empty} empty, {leaks} owner leaks, " +
        $"p50 {latencies[latencies.Count / 2]:0.0} ms, p95 {latencies[(int)(latencies.Count * 0.95)]:0.0} ms");
}
