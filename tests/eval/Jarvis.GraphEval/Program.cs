using System.Reflection;
using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.Memory;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;

// Knowledge-graph eval: feeds ordered memories through KnowledgeGraphExtractor one at a time (like the indexer after
// each new memory), applies the facts with the same rules as KnowledgeGraphRepository.MergeAsync, and checks the end
// state without depending on exact predicate names: a moved person's old city is no longer current, liking three
// things keeps all three, the same person is one entity.
//   CODEX_HOME=... EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.GraphEval [-- --runs N]
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var scenarios = JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json")), json)!.Scenarios;
var runsIndex = Array.IndexOf(args, "--runs");
var runs = runsIndex >= 0 ? int.Parse(args[runsIndex + 1]) : 1;
var codex = new CodexCliChatClient(new CodexExecutable(Environment.GetEnvironmentVariable("EXTRACTION_EVAL_CODEX") ?? "codex",
    CodexExecutable.DefaultManagedDirectory()), Environment.GetEnvironmentVariable("EXTRACTION_EVAL_MODEL"),
    enableWebSearch: false);

int passedChecks = 0, totalChecks = 0, passedScenarios = 0, totalScenarios = 0;
var predicates = new Dictionary<string, int>();
for (var run = 0; run < runs; run++)
foreach (var scenario in scenarios)
{
    var graph = new SimulatedGraph();
    var extractor = new KnowledgeGraphExtractor(new Resolver(codex), graph.AsRepository());
    var owner = Guid.CreateVersion7();
    var day = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero).AddDays(-scenario.Memories.Length);
    foreach (var content in scenario.Memories)
    {
        day = day.AddDays(1);
        var memory = new MemoryRecord(Guid.CreateVersion7(), owner, "fact", content, 0.6f, 0.9f, "conversation", null,
            day, day, null, false);
        var facts = await extractor.ExtractAsync(owner, [memory], CancellationToken.None);
        graph.Merge(facts[memory.Id]);
    }
    foreach (var predicate in graph.Relations.Select(relation => relation.Predicate))
        predicates[predicate] = predicates.GetValueOrDefault(predicate) + 1;

    var failures = new List<string>();
    foreach (var check in scenario.Checks)
        foreach (var (ok, label) in graph.Evaluate(check))
        {
            totalChecks++;
            if (ok) passedChecks++; else failures.Add(label);
        }
    totalScenarios++;
    if (failures.Count == 0) passedScenarios++;
    Console.WriteLine($"{(failures.Count == 0 ? "pass" : "FAIL")}  {scenario.Id,-18} {string.Join("; ", failures)}");
    if (failures.Count > 0 || args.Contains("--verbose"))
        foreach (var relation in graph.Relations)
            Console.WriteLine($"        {relation.Subject} {relation.Predicate} {relation.Object}" +
                              $"{(relation.Exclusive ? " [exclusive]" : "")}{(relation.Current ? "" : " (closed)")}");
}
Console.WriteLine();
Console.WriteLine($"Scenarios passed {passedScenarios}/{totalScenarios}, checks passed {passedChecks}/{totalChecks}");
Console.WriteLine($"Distinct predicates: {predicates.Count} ({string.Join(", ", predicates.OrderByDescending(p => p.Value).Select(p => $"{p.Key}×{p.Value}"))})");

internal sealed record ScenarioFile(Scenario[] Scenarios);
internal sealed record Scenario(string Id, string[] Memories, Check[] Checks);
internal sealed record Check(string? Subject = null, string[]? Current = null, string[]? NotCurrent = null,
    string[]? Related = null, string[]? Entities = null, int? MaxEntitiesMatching = null);

internal sealed class Relation(string subject, string predicate, string @object, bool exclusive, DateTimeOffset validFrom)
{
    public string Subject { get; } = subject;
    public string Predicate { get; } = predicate;
    public string Object { get; } = @object;
    public bool Exclusive { get; } = exclusive;
    public DateTimeOffset ValidFrom { get; } = validFrom;
    public bool Current { get; set; } = true;
}

/// <summary>The merge rules of KnowledgeGraphRepository.MergeAsync over in-memory entities and relations.</summary>
internal sealed class SimulatedGraph
{
    public Dictionary<string, (string Name, string Type)> Entities { get; } = [];
    public List<Relation> Relations { get; } = [];

    public void Merge(IReadOnlyList<GraphFact> facts)
    {
        foreach (var fact in facts)
        {
            var predicate = GraphNames.Predicate(fact.Predicate);
            if (predicate.Length == 0) continue;
            var subject = Upsert(fact.Subject, fact.SubjectType);
            var obj = fact.ObjectIsEntity ? Upsert(fact.Object, fact.ObjectType ?? "thing") : fact.Object.Trim();
            if (subject == obj) continue;
            if (fact.Ends)
            {
                var open = Relations.Where(r => r.Current && Key(r.Subject) == Key(subject) && Key(r.Object) == Key(obj)).ToList();
                foreach (var relation in open.Where(r => r.Predicate == predicate).ToList() is { Count: > 0 } same ? same : open)
                    relation.Current = false;
                continue;
            }
            var current = Relations.Where(r => r.Current && Key(r.Subject) == Key(subject) && r.Predicate == predicate).ToList();
            if (current.Any(r => string.Equals(Key(r.Object), Key(obj), StringComparison.Ordinal))) continue;
            if (fact.Exclusive && !GraphNames.ManyValued.Contains(predicate))
                foreach (var previous in current) previous.Current = false;
            Relations.Add(new Relation(subject, predicate, obj, fact.Exclusive, fact.ValidFrom));
        }
    }

    private string Upsert(string name, string type)
    {
        var key = GraphNames.Key(name);
        if (key == GraphNames.UserKey) return "user";
        if (!Entities.ContainsKey(key)) Entities[key] = (name.Trim(), type);
        return Entities[key].Name;
    }

    private static string Key(string name) => GraphNames.Key(name);

    private static bool Mentions(string text, string pattern) =>
        pattern.Split('|').Any(option => text.Contains(option, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<Relation> About(string subject) =>
        Relations.Where(r => Key(r.Subject) == Key(subject) ||
                             (subject != "user" && Mentions(r.Subject, subject)));

    public IEnumerable<(bool Ok, string Label)> Evaluate(Check check)
    {
        var subject = check.Subject ?? "user";
        foreach (var value in check.Current ?? [])
            yield return (About(subject).Any(r => r.Current && Mentions(r.Object, value)), $"{subject}: '{value}' not current");
        foreach (var value in check.NotCurrent ?? [])
            yield return (!About(subject).Any(r => r.Current && Mentions(r.Object, value)), $"{subject}: '{value}' still current");
        foreach (var value in check.Related ?? [])
            yield return (Relations.Any(r => r.Current &&
                    ((Key(r.Subject) == Key(subject) && Mentions(r.Object, value)) ||
                     (Mentions(r.Subject, value) && Key(r.Object) == Key(subject)))),
                $"{subject}: no current link with '{value}'");
        foreach (var value in check.Entities ?? [])
        {
            var matching = Entities.Values.Count(entity => Mentions(entity.Name, value));
            yield return (matching >= 1 && matching <= (check.MaxEntitiesMatching ?? int.MaxValue),
                $"{matching} entities named like '{value}'");
        }
    }

    public IKnowledgeGraphRepository AsRepository() => GraphProxy.Create(this);
}

/// <summary>Answers the extractor's known-entity lookup from the simulated graph; nothing else is used.</summary>
internal class GraphProxy : DispatchProxy
{
    private SimulatedGraph _graph = null!;

    public static IKnowledgeGraphRepository Create(SimulatedGraph graph)
    {
        var proxy = Create<IKnowledgeGraphRepository, GraphProxy>();
        ((GraphProxy)(object)proxy)._graph = graph;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(IKnowledgeGraphRepository.ListEntitiesAsync)
            ? Task.FromResult<IReadOnlyList<GraphEntityRecord>>(_graph.Entities.Values.Select(entity =>
                new GraphEntityRecord(Guid.NewGuid(), entity.Name, entity.Type, null, [], 0,
                    DateTimeOffset.UtcNow)).ToArray())
            : throw new NotSupportedException(targetMethod?.Name);
}

internal sealed class Resolver(IChatClient chat) : IChatClientResolver
{
    public Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose, CancellationToken cancellationToken,
        ModelSettings? overlay = null) => Task.FromResult(chat);
    public Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<EmbeddingModel?>(null);
}
