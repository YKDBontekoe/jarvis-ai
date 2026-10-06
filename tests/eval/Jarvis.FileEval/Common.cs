using System.Text.Json;
using Npgsql;
using Pgvector.Npgsql;

namespace Jarvis.FileEval;

internal sealed record Doc(string DocId, string Name, string Source, string Text, List<Passage> Passages);

internal sealed record Passage(string Pid, int Start, int End);

internal sealed record EvalQuery(string Qid, string Source, string Text, Dictionary<string, int> Rel);

/// <summary>One indexed chunk as exported for the Python side (embedding, rerank, scoring); line number = row id.</summary>
internal sealed record ChunkRow(Guid Id, int Doc, int Index, int Start, int End, string Content);

internal sealed record RunLine(string Qid, double Ms, int Leaks, double[][] Hits);

internal static class Json
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static IEnumerable<T> ReadLines<T>(string path) =>
        File.ReadLines(path).Where(line => line.Length > 0).Select(line => JsonSerializer.Deserialize<T>(line, Web)!);
}

/// <summary>Where one eval library, chunking configuration and scratch database live.</summary>
internal sealed class Workspace
{
    public static readonly Guid Owner = new("00000000-0000-7000-8000-000000000001");
    public static readonly Guid ForeignOwner = new("00000000-0000-7000-8000-000000000002");
    public const string TemplateDb = "fe_template";

    private readonly string _host;

    public Workspace(string lib, string tag)
    {
        _host = Environment.GetEnvironmentVariable("FILE_EVAL_HOST")
            ?? throw new InvalidOperationException(
                "Set FILE_EVAL_HOST to a scratch server, e.g. \"Host=localhost;Username=jarvis;Password=...\" (superuser).");
        Lib = Path.GetFullPath(lib);
        Tag = tag;
        DbName = Sanitize($"fe_{Path.GetFileName(Lib)}_{tag}");
        IndexDir = Path.Combine(Lib, "idx-" + tag);
        RunsDir = Path.Combine(Lib, "runs-" + tag);
    }

    public string Lib { get; }
    public string Tag { get; }
    public string DbName { get; }
    public string IndexDir { get; }
    public string RunsDir { get; }
    public string Connection(string database) => $"{_host};Database={database};Maximum Pool Size=16";
    public string ChunksPath => Path.Combine(IndexDir, "chunks.jsonl");

    public NpgsqlDataSource DataSource(string? database = null)
    {
        var builder = new NpgsqlDataSourceBuilder(Connection(database ?? DbName));
        builder.UseVector();
        return builder.Build();
    }

    public IEnumerable<Doc> Docs() => Json.ReadLines<Doc>(Path.Combine(Lib, "docs.jsonl"));
    public List<EvalQuery> Queries() => Json.ReadLines<EvalQuery>(Path.Combine(Lib, "queries.jsonl")).ToList();

    private static string Sanitize(string name)
    {
        var clean = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        return clean.Length > 63 ? clean[..63] : clean;
    }
}

internal static class Args
{
    public static string? Get(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static string Require(string[] args, string name) =>
        Get(args, name) ?? throw new ArgumentException($"Missing {name}.");

    public static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--opt") continue;
            var pair = args[i + 1].Split('=', 2);
            options[pair[0]] = pair.Length > 1 ? pair[1] : "true";
        }
        return options;
    }
}
