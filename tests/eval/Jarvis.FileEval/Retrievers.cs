using System.Globalization;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Memory;
using Jarvis.Infrastructure.Files;
using Jarvis.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Jarvis.FileEval;

internal readonly record struct Hit(Guid Id, double Score);

internal interface IRetriever
{
    Task<List<Hit>> SearchAsync(int queryIndex, EvalQuery query, int limit, Guid[]? files = null);
}

internal static class Retrievers
{
    /// <summary>
    /// Variant grammar (all options come from repeated --opt key=value):
    ///   baseline                       production FileSearchService (websearch_to_tsquery, ts_rank, 'simple')
    ///   lex  q=and|plain|or|mem  rank=ts|cd|bm25|pgbm25  cfg=simple|english|dutch  norm=0  cand=300  k1=1.2  b=0.75
    ///        (pgbm25 = the pg_textsearch extension's bm25 index)
    ///   sem  model=NAME iter=off|relaxed|strict ef=40 maxscan=20000   pgvector cosine over the chunk embeddings
    ///   hyb  (lex options) model=NAME wl=1 ws=1 rrfk=60 depth=30   reciprocal rank fusion of the two lists
    /// </summary>
    public static async Task<IRetriever> CreateAsync(Workspace workspace, NpgsqlDataSource source, string variant,
        Dictionary<string, string> options, IReadOnlyList<EvalQuery> queries)
    {
        var retriever = await CreateCoreAsync(workspace, source, variant, options, queries);
        if (!options.TryGetValue("rw", out var path)) return retriever;
        var rewrites = JsonSerializer.Deserialize<Dictionary<string, string[]>>(await File.ReadAllTextAsync(path))!;
        return new RewritingRetriever(retriever, rewrites, Opt(options, "rwmode", "rrf") != "only");
    }

    private static async Task<IRetriever> CreateCoreAsync(Workspace workspace, NpgsqlDataSource source, string variant,
        Dictionary<string, string> options, IReadOnlyList<EvalQuery> queries)
    {
        switch (variant)
        {
            case "baseline":
                return new BaselineRetriever(workspace);
            case "lex":
                return await LexicalRetriever.CreateAsync(source, options);
            case "sem":
                return await SemanticRetriever.CreateAsync(workspace, source, options, queries.Count);
            case "hyb":
                return new HybridRetriever(await LexicalRetriever.CreateAsync(source, options),
                    await SemanticRetriever.CreateAsync(workspace, source, options, queries.Count), options);
            default:
                throw new ArgumentException($"Unknown variant {variant}.");
        }
    }

    public static string Opt(Dictionary<string, string> options, string key, string fallback) =>
        options.TryGetValue(key, out var value) ? value : fallback;

    public static double Num(Dictionary<string, string> options, string key, double fallback) =>
        options.TryGetValue(key, out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
}

/// <summary>The code path the agent's SearchFiles tool takes today, through a fresh scoped DbContext per call.</summary>
internal sealed class BaselineRetriever(Workspace workspace) : IRetriever
{
    public async Task<List<Hit>> SearchAsync(int queryIndex, EvalQuery query, int limit, Guid[]? files = null)
    {
        await using var db = Indexing.CreateDb(workspace);
        var service = new FileSearchService(new FileContentRepository(db));
        var hits = await service.SearchAsync(Workspace.Owner, query.Text, CancellationToken.None, files);
        return hits.Select(hit => new Hit(hit.ChunkId, hit.Score)).ToList();
    }
}

internal sealed class LexicalRetriever : IRetriever
{
    private static readonly Dictionary<string, string> Columns = new()
        { ["simple"] = "search_text", ["english"] = "search_en", ["dutch"] = "search_nl" };

    private readonly NpgsqlDataSource _source;
    private readonly string _queryMode, _rank, _config, _column;
    private readonly int _norm, _candidates;
    private readonly double _k1, _b, _cap;
    private long _chunkCount;
    private double _averageLength;

    private LexicalRetriever(NpgsqlDataSource source, Dictionary<string, string> o)
    {
        _source = source;
        _queryMode = Retrievers.Opt(o, "q", "or");
        _rank = Retrievers.Opt(o, "rank", "ts");
        _config = Retrievers.Opt(o, "cfg", "simple");
        _column = Columns[_config];
        _norm = (int)Retrievers.Num(o, "norm", 0);
        _candidates = (int)Retrievers.Num(o, "cand", 300);
        _k1 = Retrievers.Num(o, "k1", 1.2);
        _b = Retrievers.Num(o, "b", 0.75);
        _cap = Retrievers.Num(o, "cap", 0.1);
    }

    public static async Task<LexicalRetriever> CreateAsync(NpgsqlDataSource source, Dictionary<string, string> options)
    {
        var retriever = new LexicalRetriever(source, options);
        if (retriever._rank == "pgbm25") await Indexing.EnsureBm25Async(source, retriever._config);
        await using var command = source.CreateCommand(
            "SELECT count(*), coalesce(avg(length(content)), 1) FROM file_content_chunks WHERE owner_id = $1");
        command.Parameters.AddWithValue(Workspace.Owner);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        retriever._chunkCount = reader.GetInt64(0);
        retriever._averageLength = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
        return retriever;
    }

    public Task<List<Hit>> SearchAsync(int queryIndex, EvalQuery query, int limit, Guid[]? files = null) =>
        SearchAsync(query.Text, limit, files);

    private static NpgsqlParameter FilesParameter(Guid[] files) =>
        new("files", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = files };

    public async Task<List<Hit>> SearchAsync(string text, int limit, Guid[]? files = null)
    {
        var scope = files is null ? string.Empty : "AND c.file_id = ANY(@files)";
        // MemoryQuery is the memory pipeline's analysis: stopwords (nl + en) dropped, at most 16 terms, optional prefix
        // forms and Dutch-English expansion. "or" keeps only the literal original terms.
        var parsed = MemoryQuery.Parse(text);
        var literal = parsed.Terms.Where(term => term.Weight >= 1).Select(term => term.Term).ToArray();
        string expression, argument;
        switch (_queryMode)
        {
            case "and": expression = "websearch_to_tsquery(@cfg::regconfig, @q)"; argument = text; break;
            case "plain": expression = "plainto_tsquery(@cfg::regconfig, @q)"; argument = text; break;
            case "or":
                if (literal.Length == 0) return [];
                expression = "to_tsquery(@cfg::regconfig, @q)"; argument = string.Join(" | ", literal); break;
            case "mem":
                if (parsed.IsEmpty) return [];
                expression = "to_tsquery(@cfg::regconfig, @q)"; argument = string.Join(" | ", parsed.Terms.Select(t => t.TsQuery)); break;
            default: throw new ArgumentException($"Unknown q={_queryMode}.");
        }

        if (_rank == "bm25") return await Bm25Async(string.Join(' ', literal), limit, files);
        if (_rank == "pgbm25")
        {
            if (literal.Length == 0) return [];
            await using var bm25 = _source.CreateCommand($"""
                SELECT c."Id", -(c.content <@> to_bm25query(@q, @index)) AS score
                FROM file_content_chunks c JOIN files f ON f."Id" = c.file_id AND f.owner_id = c.owner_id
                WHERE c.owner_id = @o AND f.processing_status = 'ready' {scope}
                ORDER BY c.content <@> to_bm25query(@q, @index) LIMIT @limit
                """);
            bm25.Parameters.AddWithValue("q", string.Join(' ', literal));
            bm25.Parameters.AddWithValue("index", $"ix_bm25_{_config}");
            bm25.Parameters.AddWithValue("o", Workspace.Owner);
            bm25.Parameters.AddWithValue("limit", limit);
            if (files is not null) bm25.Parameters.Add(FilesParameter(files));
            return await ReadHitsAsync(bm25);
        }

        var rankFunction = _rank == "cd" ? "ts_rank_cd" : "ts_rank";
        await using var command = _source.CreateCommand($"""
            WITH q AS MATERIALIZED (SELECT {expression} AS query)
            SELECT c."Id", {rankFunction}(c.{_column}, q.query, {_norm}) AS score
            FROM q, file_content_chunks c JOIN files f ON f."Id" = c.file_id AND f.owner_id = c.owner_id
            WHERE c.owner_id = @o AND f.processing_status = 'ready' AND c.{_column} @@ q.query {scope}
            ORDER BY score DESC LIMIT @limit
            """);
        command.Parameters.AddWithValue("cfg", _config);
        command.Parameters.AddWithValue("q", argument);
        command.Parameters.AddWithValue("o", Workspace.Owner);
        command.Parameters.AddWithValue("limit", limit);
        if (files is not null) command.Parameters.Add(FilesParameter(files));
        return await ReadHitsAsync(command);
    }

    /// <summary>
    /// BM25 with real inverse document frequency, which ts_rank lacks. Stage 1 gathers candidates through the GIN index
    /// (per-term postings, scored by summed IDF, common terms skipped); stage 2 scores the best candidates with BM25 from
    /// the tsvector's term positions and the chunk length.
    /// </summary>
    private async Task<List<Hit>> Bm25Async(string literalTerms, int limit, Guid[]? files)
    {
        var scope = files is null ? string.Empty : "AND c.file_id = ANY(@files)";
        if (literalTerms.Length == 0) return [];
        await using var connection = await _source.OpenConnectionAsync();
        var lexemes = new List<string>();
        await using (var lexemeCommand = new NpgsqlCommand(
            "SELECT lexeme FROM unnest(to_tsvector(@cfg::regconfig, @text))", connection))
        {
            lexemeCommand.Parameters.AddWithValue("cfg", _config);
            lexemeCommand.Parameters.AddWithValue("text", literalTerms);
            await using var reader = await lexemeCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync()) lexemes.Add(reader.GetString(0));
        }
        if (lexemes.Count == 0) return [];

        var idf = new Dictionary<string, double>();
        await using (var dfCommand = new NpgsqlCommand($"SELECT word, ndoc FROM bench_df_{_config} WHERE word = ANY(@w)", connection))
        {
            dfCommand.Parameters.AddWithValue("w", lexemes.ToArray());
            await using var reader = await dfCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var df = reader.GetInt32(1);
                idf[reader.GetString(0)] = Math.Log(1 + (_chunkCount - df + 0.5) / (df + 0.5));
            }
        }
        if (idf.Count == 0) return [];
        var all = idf.ToArray();
        // df <= cap * N: rare enough to drive candidate generation; if every term is common, use them all.
        var driving = all.Where(pair => pair.Value >= Math.Log(1 + (_chunkCount * (1 - _cap) + 0.5) / (_chunkCount * _cap + 0.5))).ToArray();
        if (driving.Length == 0) driving = all;

        var candidates = new List<Guid>();
        await using (var candidateCommand = new NpgsqlCommand($"""
            SELECT hit.id FROM (
                SELECT x.id, sum(t.idf) AS s
                FROM unnest(@lex::text[], @idf::float8[]) AS t(lexeme, idf)
                CROSS JOIN LATERAL (
                    SELECT c."Id" FROM file_content_chunks c
                    WHERE c.owner_id = @o {scope} AND c.{_column} @@ ('''' || replace(t.lexeme, '''', '''''') || '''')::tsquery) AS x(id)
                GROUP BY x.id ORDER BY s DESC LIMIT @cand) AS hit
            """, connection))
        {
            candidateCommand.Parameters.AddWithValue("lex", driving.Select(p => p.Key).ToArray());
            candidateCommand.Parameters.AddWithValue("idf", driving.Select(p => p.Value).ToArray());
            candidateCommand.Parameters.AddWithValue("o", Workspace.Owner);
            candidateCommand.Parameters.AddWithValue("cand", _candidates);
            if (files is not null) candidateCommand.Parameters.Add(FilesParameter(files));
            await using var reader = await candidateCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync()) candidates.Add(reader.GetGuid(0));
        }
        if (candidates.Count == 0) return [];

        await using var scoreCommand = new NpgsqlCommand($"""
            SELECT c."Id", sum(q.idf * (cardinality(u.positions) * (@k1 + 1)) /
                   (cardinality(u.positions) + @k1 * (1 - @b + @b * length(c.content) / @avg))) AS score
            FROM file_content_chunks c
            JOIN files f ON f."Id" = c.file_id AND f.owner_id = c.owner_id
            CROSS JOIN LATERAL unnest(c.{_column}) AS u(lexeme, positions, weights)
            JOIN unnest(@lex::text[], @idf::float8[]) AS q(lexeme, idf) ON q.lexeme = u.lexeme
            WHERE c."Id" = ANY(@ids) AND c.owner_id = @o AND f.processing_status = 'ready'
            GROUP BY c."Id" ORDER BY score DESC LIMIT @limit
            """, connection);
        scoreCommand.Parameters.AddWithValue("k1", _k1);
        scoreCommand.Parameters.AddWithValue("b", _b);
        scoreCommand.Parameters.AddWithValue("avg", _averageLength);
        scoreCommand.Parameters.AddWithValue("lex", all.Select(p => p.Key).ToArray());
        scoreCommand.Parameters.AddWithValue("idf", all.Select(p => p.Value).ToArray());
        scoreCommand.Parameters.AddWithValue("ids", candidates.ToArray());
        scoreCommand.Parameters.AddWithValue("o", Workspace.Owner);
        scoreCommand.Parameters.AddWithValue("limit", limit);
        return await ReadHitsAsync(scoreCommand);
    }

    internal static async Task<List<Hit>> ReadHitsAsync(NpgsqlCommand command)
    {
        var hits = new List<Hit>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) hits.Add(new Hit(reader.GetGuid(0), Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture)));
        return hits;
    }
}

internal sealed class SemanticRetriever : IRetriever
{
    private readonly NpgsqlDataSource _source;
    private readonly float[][] _queryVectors;
    private readonly string _settings;

    private SemanticRetriever(NpgsqlDataSource source, float[][] queryVectors, Dictionary<string, string> o)
    {
        _source = source;
        _queryVectors = queryVectors;
        // pgvector 0.8 iterative scans keep walking the HNSW graph until LIMIT rows survive the WHERE clause.
        var iteration = Retrievers.Opt(o, "iter", "off");
        _settings = $"SET hnsw.ef_search = {(int)Retrievers.Num(o, "ef", 40)}; " +
            $"SET hnsw.iterative_scan = '{(iteration == "off" ? "off" : iteration + "_order")}'; " +
            $"SET hnsw.max_scan_tuples = {(int)Retrievers.Num(o, "maxscan", 20000)}; ";
    }

    public static async Task<SemanticRetriever> CreateAsync(Workspace workspace, NpgsqlDataSource source,
        Dictionary<string, string> options, int queryCount)
    {
        var model = options.TryGetValue("model", out var m) ? m : throw new ArgumentException("sem and hyb need --opt model=NAME.");
        await using (var command = source.CreateCommand("SELECT value FROM bench_meta WHERE key = 'embedding_model'"))
        {
            var loaded = await command.ExecuteScalarAsync() as string;
            if (loaded != model) throw new InvalidOperationException($"Database holds {loaded ?? "no"} embeddings, not {model}; run embed-load first.");
        }
        var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.Lib, $"qemb-{model}.json"))).RootElement;
        var dim = meta.GetProperty("dim").GetInt32();
        var raw = await File.ReadAllBytesAsync(Path.Combine(workspace.Lib, $"qemb-{model}.f32"));
        if (raw.Length != queryCount * dim * 4) throw new InvalidOperationException("Query embeddings do not match the query set.");
        var vectors = new float[queryCount][];
        for (var i = 0; i < queryCount; i++)
        {
            vectors[i] = new float[1536];
            Buffer.BlockCopy(raw, i * dim * 4, vectors[i], 0, dim * 4);
        }
        return new SemanticRetriever(source, vectors, options);
    }

    public async Task<List<Hit>> SearchAsync(int queryIndex, EvalQuery query, int limit, Guid[]? files = null)
    {
        var scope = files is null ? string.Empty : "AND c.file_id = ANY(@files)";
        await using var command = _source.CreateCommand(_settings + $"""
            SELECT c."Id", 1 - (c.embedding <=> @v) AS similarity
            FROM file_content_chunks c
            WHERE c.owner_id = @o AND c.embedding IS NOT NULL {scope}
              AND EXISTS (SELECT 1 FROM files f WHERE f."Id" = c.file_id AND f.owner_id = c.owner_id AND f.processing_status = 'ready')
            ORDER BY c.embedding <=> @v LIMIT @limit
            """);
        if (files is not null) command.Parameters.Add(new NpgsqlParameter("files", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = files });
        command.Parameters.Add(new NpgsqlParameter("v", new Vector(_queryVectors[queryIndex])));
        command.Parameters.AddWithValue("o", Workspace.Owner);
        command.Parameters.AddWithValue("limit", limit);
        return await LexicalRetriever.ReadHitsAsync(command);
    }
}

/// <summary>
/// Reciprocal rank fusion of the lexical and semantic lists, in the shape FileSearchService.AddRanks already has
/// (score += 1 / (60 + rank)). The two lists are awaited one after the other because the production repositories share a
/// scoped DbContext.
/// </summary>
internal sealed class HybridRetriever(LexicalRetriever lexical, SemanticRetriever semantic, Dictionary<string, string> o) : IRetriever
{
    private readonly int _depth = (int)Retrievers.Num(o, "depth", 30);
    private readonly int _k = (int)Retrievers.Num(o, "rrfk", 60);
    private readonly double _wl = Retrievers.Num(o, "wl", 1), _ws = Retrievers.Num(o, "ws", 1);

    public async Task<List<Hit>> SearchAsync(int queryIndex, EvalQuery query, int limit, Guid[]? files = null)
    {
        var lex = await lexical.SearchAsync(query.Text, _depth, files);
        var sem = await semantic.SearchAsync(queryIndex, query, _depth, files);
        var scores = new Dictionary<Guid, double>();
        Add(lex, _wl);
        Add(sem, _ws);
        return scores.OrderByDescending(pair => pair.Value).Take(limit).Select(pair => new Hit(pair.Key, pair.Value)).ToList();

        void Add(List<Hit> list, double weight)
        {
            for (var rank = 0; rank < list.Count; rank++)
                scores[list[rank].Id] = scores.GetValueOrDefault(list[rank].Id) + weight / (_k + rank + 1);
        }
    }
}

/// <summary>
/// Agent-style search: the original question plus the search queries a model wrote for it (from the question alone), each
/// run through the same retriever and fused with reciprocal rank fusion. The semantic leg of a hybrid keeps the original
/// question's vector, so only keyword legs see the rewrites. --opt rw=PATH (qid -> [queries]) --opt rwmode=rrf|only.
/// </summary>
internal sealed class RewritingRetriever(IRetriever inner, Dictionary<string, string[]> rewrites, bool includeOriginal) : IRetriever
{
    public async Task<List<Hit>> SearchAsync(int queryIndex, EvalQuery query, int limit, Guid[]? files = null)
    {
        var texts = rewrites.TryGetValue(query.Qid, out var extra) ? extra : [];
        var variants = (includeOriginal ? new[] { query.Text } : []).Concat(texts).ToArray();
        if (variants.Length == 0) variants = [query.Text];
        var scores = new Dictionary<Guid, double>();
        foreach (var text in variants)
        {
            var hits = await inner.SearchAsync(queryIndex, query with { Text = text }, limit, files);
            for (var rank = 0; rank < hits.Count; rank++)
                scores[hits[rank].Id] = scores.GetValueOrDefault(hits[rank].Id) + 1d / (60 + rank + 1);
        }
        return scores.OrderByDescending(pair => pair.Value).Take(limit).Select(pair => new Hit(pair.Key, pair.Value)).ToList();
    }
}
