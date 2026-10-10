using System.ClientModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Agents;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Audit;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;

// Offline eval for memory extraction (ConversationMemoryExtractor) against a real model. Each case gives existing
// memories, the turns before a message and the message, and lists the writes extraction should make. Search returns
// every memory of the case, so this measures the extraction decision, not retrieval (Jarvis.MemoryEval covers that).
//   EXTRACTION_EVAL_API_KEY=... EXTRACTION_EVAL_MODEL=openai/gpt-5-mini dotnet run --project tests/eval/Jarvis.ExtractionEval
// EXTRACTION_EVAL_BASE_URL defaults to OpenRouter. "--runs N" repeats every case, "--case id" runs one case,
// "--dry-run" only validates the dataset.
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var datasetIndex = Array.IndexOf(args, "--dataset");
var datasetPath = datasetIndex >= 0 ? args[datasetIndex + 1] : Path.Combine(AppContext.BaseDirectory, "dataset.json");
var dataset = JsonSerializer.Deserialize<Dataset>(File.ReadAllText(datasetPath), json)!;
var zone = TimeZoneInfo.FindSystemTimeZoneById(dataset.TimeZone);
var runsIndex = Array.IndexOf(args, "--runs");
var runs = runsIndex >= 0 ? int.Parse(args[runsIndex + 1], CultureInfo.InvariantCulture) : 1;
var caseIndex = Array.IndexOf(args, "--case");
var cases = caseIndex >= 0 ? dataset.Cases.Where(item => item.Id == args[caseIndex + 1]).ToArray() : dataset.Cases;

foreach (var item in dataset.Cases) Validate(item);
Console.WriteLine($"{dataset.Cases.Length} cases are valid.");
if (args.Contains("--dry-run")) return;

var apiKey = Environment.GetEnvironmentVariable("EXTRACTION_EVAL_API_KEY")
    ?? throw new InvalidOperationException("Set EXTRACTION_EVAL_API_KEY (and EXTRACTION_EVAL_MODEL).");
var model = Environment.GetEnvironmentVariable("EXTRACTION_EVAL_MODEL")
    ?? throw new InvalidOperationException("Set EXTRACTION_EVAL_MODEL, for example openai/gpt-5-mini.");
var baseUrl = Environment.GetEnvironmentVariable("EXTRACTION_EVAL_BASE_URL") ?? "https://openrouter.ai/api/v1";
var chat = new OpenAIClient(new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(baseUrl.TrimEnd('/') + "/") })
    .GetChatClient(model).AsIChatClient();

var results = new List<CaseResult>();
for (var run = 0; run < runs; run++)
foreach (var item in cases)
{
    var stopwatch = Stopwatch.StartNew();
    var writes = await RunCaseAsync(item);
    var result = Score(item, writes, stopwatch.Elapsed);
    results.Add(result);
    Console.WriteLine($"{(result.Passed ? "pass" : "FAIL")}  {item.Id,-26} {result.Elapsed.TotalSeconds,5:0.0}s  " +
                      string.Join("; ", result.Notes));
}

var expected = results.Sum(result => result.Expected);
Console.WriteLine();
Console.WriteLine($"Model {model}, {results.Count} case runs");
Console.WriteLine($"Cases passed     {results.Count(result => result.Passed)}/{results.Count} " +
                  $"({(double)results.Count(result => result.Passed) / results.Count:0.00})");
Console.WriteLine($"Write recall     {results.Sum(result => result.Matched)}/{expected}");
Console.WriteLine($"Extra writes     {results.Sum(result => result.Extra)}");
Console.WriteLine($"Harmful writes   {results.Sum(result => result.Harmful)}  (replaced or ended a memory the case did not expect)");
Console.WriteLine($"Relative dates   {results.Sum(result => result.Forbidden)}  (stored text with a forbidden phrase)");
Console.WriteLine($"Latency p50      {Percentile(results, 0.5):0.0}s, p95 {Percentile(results, 0.95):0.0}s");

async Task<IReadOnlyList<Write>> RunCaseAsync(EvalCase item)
{
    var owner = Guid.CreateVersion7();
    var now = dataset.Today;
    var existing = (item.Memories ?? []).Select(memory => (memory.Key, Record: new MemoryRecord(Guid.CreateVersion7(),
        owner, memory.Kind, memory.Content, 0.6f, 0.9f, "conversation", null, now.AddDays(-30), now.AddDays(-30), null,
        memory.Pinned))).ToArray();
    var store = new RecordingMemories(existing.Select(memory => memory.Record).ToArray());
    var audit = new RecordingAudit();
    var extractor = new ConversationMemoryExtractor(new FixedResolver(chat), store, audit,
        NullLogger<ConversationMemoryExtractor>.Instance, FixedBriefing.Create(dataset.TimeZone), new FixedClock(now));
    var context = (item.Context ?? []).Select(turn => new MemoryExtractionTurn(turn.Role, turn.Content)).ToArray();
    await extractor.ExtractAndStoreAsync(owner, Guid.CreateVersion7(), item.Message, CancellationToken.None,
        context: context);

    var keys = existing.ToDictionary(memory => memory.Record.Id, memory => memory.Key);
    if (store.Writes.Count != audit.Actions.Count)
        throw new InvalidOperationException($"{item.Id}: every write must be audited once.");
    return store.Writes.Zip(audit.Actions, (write, action) => write with
    {
        Op = action["memory.".Length..],
        Target = write.TargetId is { } id ? keys.GetValueOrDefault(id) : null
    }).ToArray();
}

CaseResult Score(EvalCase item, IReadOnlyList<Write> writes, TimeSpan elapsed)
{
    var notes = new List<string>();
    var unmatched = writes.ToList();
    var matched = 0;
    foreach (var expectation in item.Expect)
    {
        var hit = unmatched.FirstOrDefault(write => Matches(expectation, write));
        if (hit is null)
        {
            notes.Add($"missing {string.Join('|', expectation.Ops)}{(expectation.Target is null ? "" : " " + expectation.Target)}");
            continue;
        }
        unmatched.Remove(hit);
        matched++;
    }
    var extra = item.AllowExtra ? 0 : unmatched.Count;
    var expectedTargets = item.Expect.Select(expectation => expectation.Target).OfType<string>().ToHashSet();
    var harmful = writes.Count(write => write.Target is not null && !expectedTargets.Contains(write.Target));
    var forbidden = writes.Count(write => (item.MustNotContain ?? [])
        .Any(phrase => write.Content.Contains(phrase, StringComparison.OrdinalIgnoreCase)));
    notes.AddRange(unmatched.Select(write =>
        $"{(item.AllowExtra ? "extra (allowed)" : "extra")} {write.Op}{(write.Target is null ? "" : " " + write.Target)}: \"{write.Content}\"" +
        (write.ValidUntil is { } until ? $" until {until:yyyy-MM-dd HH:mm}Z" : "")));
    if (forbidden > 0) notes.Add("forbidden phrase stored");
    var passed = matched == item.Expect.Length && extra == 0 && harmful == 0 && forbidden == 0;
    return new CaseResult(item.Id, passed, item.Expect.Length, matched, extra, harmful, forbidden, elapsed, notes);
}

bool Matches(Expectation expectation, Write write)
{
    if (!expectation.Ops.Contains(write.Op)) return false;
    if (expectation.Target is not null && expectation.Target != write.Target) return false;
    if (expectation.Contains?.Any(part => !write.Content.Contains(part, StringComparison.OrdinalIgnoreCase)) == true)
        return false;
    return expectation.ValidUntil switch
    {
        null => true,
        "none" => write.ValidUntil is null,
        "set" => write.ValidUntil is not null,
        var date => write.ValidUntil == ConversationMemoryExtractor.EndOfLocalDay(date, zone, dataset.Today)
    };
}

void Validate(EvalCase item)
{
    var keys = (item.Memories ?? []).Select(memory => memory.Key).ToHashSet();
    foreach (var expectation in item.Expect)
    {
        if (expectation.Ops.Length == 0 || expectation.Ops.Any(op => op is not ("extracted" or "enriched" or "superseded" or "expired")))
            throw new InvalidOperationException($"{item.Id}: unknown op.");
        if (expectation.Target is not null && !keys.Contains(expectation.Target))
            throw new InvalidOperationException($"{item.Id}: unknown target {expectation.Target}.");
        if (expectation.Ops.Any(op => op != "extracted") && expectation.Ops.All(op => op != "extracted") &&
            expectation.Target is null)
            throw new InvalidOperationException($"{item.Id}: a replace or expire needs a target.");
        if (expectation.ValidUntil is { } date and not ("none" or "set") &&
            ConversationMemoryExtractor.EndOfLocalDay(date, zone, dataset.Today) is null)
            throw new InvalidOperationException($"{item.Id}: validUntil {date} is not a usable future date.");
    }
}

static double Percentile(List<CaseResult> results, double percentile)
{
    var sorted = results.Select(result => result.Elapsed.TotalSeconds).Order().ToArray();
    return sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(percentile * sorted.Length))];
}

internal sealed record Dataset(DateTimeOffset Today, string TimeZone, EvalCase[] Cases);

internal sealed record EvalCase(string Id, string Message, Expectation[] Expect, SeedMemory[]? Memories = null,
    Turn[]? Context = null, string[]? MustNotContain = null, bool AllowExtra = false);

internal sealed record SeedMemory(string Key, string Kind, string Content, bool Pinned = false);

internal sealed record Turn(string Role, string Content);

internal sealed record Expectation(
    [property: JsonConverter(typeof(OneOrManyConverter))] string[] Op,
    string? Target = null, string[]? Contains = null, string? ValidUntil = null)
{
    public string[] Ops => Op;
}

internal sealed record Write(string Kind, string Content, DateTimeOffset? ValidUntil, Guid? TargetId,
    string Op = "", string? Target = null);

internal sealed record CaseResult(string Id, bool Passed, int Expected, int Matched, int Extra, int Harmful,
    int Forbidden, TimeSpan Elapsed, IReadOnlyList<string> Notes);

/// <summary>Reads "op" as either one string or a list of accepted ops.</summary>
internal sealed class OneOrManyConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? [reader.GetString()!]
            : JsonSerializer.Deserialize<string[]>(ref reader, options) ?? [];

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

internal sealed class FixedResolver(IChatClient chat) : IChatClientResolver
{
    public Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose,
        CancellationToken cancellationToken, ModelSettings? overlay = null) => Task.FromResult(chat);

    public Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<EmbeddingModel?>(null);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Answers only the owner's time zone lookup; any other briefing call fails the run.</summary>
internal class FixedBriefing : System.Reflection.DispatchProxy
{
    private string _timeZoneId = "UTC";

    public static Jarvis.Application.Workflows.IDailyBriefingRepository Create(string timeZoneId)
    {
        var proxy = Create<Jarvis.Application.Workflows.IDailyBriefingRepository, FixedBriefing>();
        ((FixedBriefing)(object)proxy)._timeZoneId = timeZoneId;
        return proxy;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(Jarvis.Application.Workflows.IDailyBriefingRepository.GetAsync)
            ? Task.FromResult<Jarvis.Application.Workflows.DailyBriefingPreferenceRecord?>(
                new((Guid)args![0]!, false, new TimeOnly(7, 0), _timeZoneId, "eval", null, null))
            : throw new NotSupportedException(targetMethod?.Name);
}

internal sealed class RecordingAudit : IAuditEventStore
{
    public List<string> Actions { get; } = [];

    public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass, bool success,
        Guid? approvalId, string? metadataJson, CancellationToken cancellationToken, Guid? agentRunId = null)
    {
        Actions.Add(action);
        return Task.FromResult<AuditEventRecord>(null!);
    }

    public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Keeps the case's memories and records every write extraction makes.</summary>
internal sealed class RecordingMemories(IReadOnlyList<MemoryRecord> memories) : IMemoryService
{
    public List<Write> Writes { get; } = [];

    public Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken) =>
        Task.FromResult(memories);

    public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
        CancellationToken cancellationToken, string? kind = null, int maxHits = MemoryRanking.MaxHits) =>
        Task.FromResult<IReadOnlyList<MemorySearchHit>>(memories.Select(memory => new MemorySearchHit(memory, 1)).ToArray());

    public Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
        DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken, string sourceType = "user",
        Guid? sourceId = null, Guid? profileId = null)
    {
        Writes.Add(new Write(kind, content, validUntil, null));
        return Task.FromResult(Record(ownerId, kind, content, validUntil));
    }

    public Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
        float importance, float confidence, CancellationToken cancellationToken, string sourceType = "conversation",
        Guid? sourceId = null)
    {
        var target = memories.FirstOrDefault(memory => memory.Id == existingId);
        if (target is null || target.IsPinned || target.Kind != kind) return Task.FromResult<MemoryRecord?>(null);
        Writes.Add(new Write(kind, content, null, existingId));
        return Task.FromResult<MemoryRecord?>(Record(ownerId, kind, content, null));
    }

    public Task<MemoryRecord?> ExpireAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var target = memories.FirstOrDefault(memory => memory.Id == id);
        if (target is null || target.IsPinned) return Task.FromResult<MemoryRecord?>(null);
        Writes.Add(new Write(target.Kind, target.Content, null, id));
        return Task.FromResult<MemoryRecord?>(target with { ValidUntil = DateTimeOffset.UtcNow });
    }

    private static MemoryRecord Record(Guid ownerId, string kind, string content, DateTimeOffset? validUntil) =>
        new(Guid.CreateVersion7(), ownerId, kind, content, 0.5f, 0.9f, "conversation", null, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, validUntil, false);

    public Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
        float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task RecordRecallAsync(Guid ownerId, IReadOnlyCollection<Guid> memoryIds,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
