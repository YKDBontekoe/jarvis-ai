using System.Reflection;
using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.Learning;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Memory;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

// Dreaming eval: runs the REM model step of DreamingService over invented memories and recent user messages, then
// applies the same filters as the deep phase to see which memories would be rewritten. Merge and supersede rewrite a
// memory without review, so the main number is harmful rewrites: a rewrite the scenario neither requires nor allows.
//   CODEX_HOME=... EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.DreamingEval [-- --runs N]
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var scenarios = JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json")), json)!.Scenarios;
var runsIndex = Array.IndexOf(args, "--runs");
var runs = runsIndex >= 0 ? int.Parse(args[runsIndex + 1]) : 1;
var codex = new CodexCliChatClient(new CodexExecutable(Environment.GetEnvironmentVariable("EXTRACTION_EVAL_CODEX") ?? "codex",
    CodexExecutable.DefaultManagedDirectory()), Environment.GetEnvironmentVariable("EXTRACTION_EVAL_MODEL"),
    enableWebSearch: false);
var now = new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero);
var service = new DreamingService(Unused<Jarvis.Application.Conversations.IConversationHistory>.Create(),
    Unused<IMemoryService>.Create(), new PersonaService(Unused<IOwnerSettingsStore>.Create()),
    Unused<IKnowledgeGraphRepository>.Create(), Unused<IOwnerSettingsStore>.Create(),
    Unused<Jarvis.Application.Workflows.INotificationRepository>.Create(),
    Unused<Jarvis.Application.Audit.IAuditEventStore>.Create(), new Resolver(codex),
    Unused<Jarvis.Application.Learning.IMemoryRecallTracker>.Create(), NullLogger<DreamingService>.Instance,
    new FixedClock(now));

int passed = 0, total = 0, required = 0, requiredHit = 0, harmful = 0, sensitive = 0, rewrites = 0;
for (var run = 0; run < runs; run++)
foreach (var scenario in scenarios)
{
    var owner = Guid.CreateVersion7();
    var stored = scenario.Memories.Select(memory => (memory.Key, Record: new MemoryRecord(Guid.CreateVersion7(), owner,
        memory.Kind, memory.Content, 0.6f, 0.9f, "conversation", Guid.CreateVersion7(), now.AddDays(-60),
        now.AddDays(-60), null, memory.Pinned))).ToArray();
    var keys = stored.ToDictionary(memory => memory.Record.Id, memory => memory.Key);
    var staged = stored.Select(memory => new DreamCandidate(memory.Key, memory.Record.Kind, memory.Record.Content,
        memory.Record.Id, memory.Record.SourceId, memory.Record.IsPinned, 0.6f, 0.9f, now.AddDays(-60), now.AddDays(-1),
        1, 1, 1, 0.5, 0.5, 0, 0)).ToList();
    var messages = scenario.Messages.Select((text, index) =>
        new Message(Guid.CreateVersion7(), "user", text)).ToList();

    var rem = await service.RemAsync(owner, new LearningSettings(), staged, stored.Select(memory => memory.Record).ToList(),
        new PersonaProfile(), messages, CancellationToken.None);

    // The deep phase's own filters: a known unpinned, non-journal target of the same kind, confidence 0.8 or more.
    var byId = stored.ToDictionary(memory => memory.Record.Id, memory => memory.Record);
    var applied = (rem.Memories ?? []).Where(item =>
            item.Action?.Trim().ToLowerInvariant() is "merge" or "supersede" &&
            item.TargetMemoryId is { } id && byId.TryGetValue(id, out var target) && !target.IsPinned &&
            target.Kind == item.Kind?.Trim().ToLowerInvariant() && item.Confidence is >= 0.8f and <= 1f &&
            !string.IsNullOrWhiteSpace(item.Content))
        .Select(item => (Target: keys[item.TargetMemoryId!.Value], Content: item.Content!.Trim(), Action: item.Action!.Trim()))
        .ToList();
    var adds = (rem.Memories ?? []).Where(item => item.Action?.Trim().ToLowerInvariant() is null or "add")
        .Select(item => item.Content ?? "").ToList();
    rewrites += applied.Count;

    var notes = new List<string>();
    foreach (var expectation in scenario.Required ?? [])
    {
        required++;
        if (applied.Any(write => Matches(expectation, write))) requiredHit++;
        else notes.Add($"missing rewrite of {expectation.Target}");
    }
    foreach (var write in applied.Where(write => !(scenario.Required ?? []).Concat(scenario.Allowed ?? [])
                 .Any(expectation => Matches(expectation, write))))
    {
        harmful++;
        notes.Add($"HARMFUL {write.Action} {write.Target}: \"{write.Content}\"");
    }
    foreach (var text in adds.Concat(applied.Select(write => write.Content))
                 .Where(text => (scenario.ForbiddenContent ?? []).Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase))))
    {
        sensitive++;
        notes.Add($"sensitive memory proposed: \"{text}\"");
    }
    total++;
    var ok = notes.Count == 0;
    if (ok) passed++;
    var info = string.Join("; ", applied.Select(write => $"{write.Action} {write.Target}: \"{write.Content}\""));
    Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {scenario.Id,-20} {string.Join("; ", notes)}{(ok && info.Length > 0 ? info : "")}");
}
Console.WriteLine();
Console.WriteLine($"Scenarios passed {passed}/{total}; required rewrites {requiredHit}/{required}; harmful rewrites {harmful}; " +
                  $"sensitive proposals {sensitive}; rewrites applied {rewrites}");

static bool Matches(Expectation expectation, (string Target, string Content, string Action) write) =>
    expectation.Target == write.Target && (expectation.Contains ?? []).All(part =>
        part.Split('|').Any(option => write.Content.Contains(option, StringComparison.OrdinalIgnoreCase)));

internal sealed record ScenarioFile(Scenario[] Scenarios);
internal sealed record Scenario(string Id, SeedMemory[] Memories, string[] Messages, Expectation[]? Required = null,
    Expectation[]? Allowed = null, string[]? ForbiddenContent = null);
internal sealed record SeedMemory(string Key, string Kind, string Content, bool Pinned = false);
internal sealed record Expectation(string Target, string[]? Contains = null);

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class Resolver(IChatClient chat) : IChatClientResolver
{
    public Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose, CancellationToken cancellationToken,
        ModelSettings? overlay = null) => Task.FromResult(chat);
    public Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<EmbeddingModel?>(null);
}

/// <summary>A dependency the REM step never calls; any call fails the run.</summary>
internal class Unused<T> : DispatchProxy where T : class
{
    public static T Create() => Create<T, Unused<T>>();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException($"{typeof(T).Name}.{targetMethod?.Name}");
}
