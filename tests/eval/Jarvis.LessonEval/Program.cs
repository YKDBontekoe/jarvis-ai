using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.Improvements;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Improvements;
using Jarvis.Application.Learning;
using Jarvis.Application.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

// Lesson eval: how Jarvis learns from turns that went wrong, over invented traces and signals.
//   mining    which tools the lesson miner picks out, and why (no model)
//   counting  how a lesson is credited or blamed for the rated turns it was shown in (no model)
//   selection which lessons reach a turn, given what the turn can use (no model)
//   drafting  the model writes one clean lesson per finding and invents no details (only with --model)
//   dotnet run --project tests/eval/Jarvis.LessonEval
//   CODEX_HOME=... EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.LessonEval -- --model
// The run exits 1 unless every rule scenario passes with no false findings (--min-pass 1, --max-false 0) and, with
// --model, at least --min-drafted (0.75) of the findings get a clean lesson with at most --max-invented (0) invented details.
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var file = JsonSerializer.Deserialize<ScenarioFile>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json")), json)!;
var useModel = args.Contains("--model");
var now = new DateTimeOffset(2026, 10, 19, 3, 0, 0, TimeSpan.Zero);
var owner = Id("owner");

int ruleTotal = 0, rulePassed = 0, expectedFindings = 0, foundFindings = 0, falseFindings = 0;

foreach (var scenario in file.Mining)
{
    var (traces, signals) = Build(scenario.Traces, scenario.Signals);
    var findings = LessonMinerEngine.Mine(traces, signals, now);
    var notes = new List<string>();
    foreach (var expectation in scenario.Expect)
    {
        expectedFindings++;
        var finding = findings.FirstOrDefault(item => item.Tool == expectation.Tool);
        if (finding is null) { notes.Add($"missing finding for {expectation.Tool}"); continue; }
        foundFindings++;
        if (finding.Incidents != expectation.Incidents)
            notes.Add($"{expectation.Tool}: {finding.Incidents} incidents, expected {expectation.Incidents}");
        if (!finding.Reasons.SequenceEqual(expectation.Reasons.Order(StringComparer.Ordinal)))
            notes.Add($"{expectation.Tool}: reasons [{string.Join(",", finding.Reasons)}], expected [{string.Join(",", expectation.Reasons)}]");
        if (expectation.ErrorKinds is { } kinds && !finding.ErrorKinds.SequenceEqual(kinds.Order(StringComparer.Ordinal)))
            notes.Add($"{expectation.Tool}: error kinds [{string.Join(",", finding.ErrorKinds)}], expected [{string.Join(",", kinds)}]");
    }
    foreach (var extra in findings.Where(item => scenario.Expect.All(expectation => expectation.Tool != item.Tool)))
    {
        falseFindings++;
        notes.Add($"FALSE finding for {extra.Tool} ({extra.Incidents} incidents)");
    }
    Score("mining", scenario.Id, notes);
}

foreach (var scenario in file.Counting)
{
    var (traces, signals) = Build(scenario.Traces, scenario.Signals);
    var lesson = Id(scenario.Lesson);
    var tally = LessonCounterEngine.Tally(traces, signals,
        new Dictionary<Guid, LessonPayload> { [lesson] = new("Check the inputs first.", ["SearchMail"]) }, now)[lesson];
    var notes = new List<string>();
    if (tally.Helpful != scenario.Expect.Helpful) notes.Add($"helpful {tally.Helpful}, expected {scenario.Expect.Helpful}");
    if (tally.Harmful != scenario.Expect.Harmful) notes.Add($"harmful {tally.Harmful}, expected {scenario.Expect.Harmful}");
    Score("counting", scenario.Id, notes);
}

foreach (var scenario in file.Selection)
{
    var entries = scenario.Lessons.Select((lesson, index) => new LessonEntry(Id(lesson.Id),
        new LessonPayload(lesson.Text, lesson.Tools, lesson.Helpful, lesson.Harmful), now.AddDays(-1))).ToList();
    var messages = scenario.Messages.Select(message => message.Call is { } call
        ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), call, new Dictionary<string, object?>())])
        : new ChatMessage(message.Role == "user" ? ChatRole.User : ChatRole.Assistant, message.Text ?? "")).ToList();
    var shown = LessonContextContributor.Select(entries, ToolGroups.ActiveGroups(messages)).Select(entry => entry.ProposalId).ToList();
    var expected = scenario.ExpectShown.Select(Id).ToList();
    var notes = new List<string>();
    if (!shown.SequenceEqual(expected))
        notes.Add($"shown [{string.Join(",", shown.Select(id => Name(scenario, id)))}], expected [{string.Join(",", scenario.ExpectShown)}]");
    Score("selection", scenario.Id, notes);
}

var checks = new List<(string Name, bool Passed, string Detail)>
{
    EvalGate.AtLeast("rule scenarios passed", ruleTotal == 0 ? 0 : (double)rulePassed / ruleTotal,
        EvalGate.Option(args, "--min-pass", 1.0)),
    EvalGate.AtMost("false findings", falseFindings, EvalGate.Option(args, "--max-false", 0))
};
Console.WriteLine();
Console.WriteLine($"Rule scenarios passed {rulePassed}/{ruleTotal}; findings found {foundFindings}/{expectedFindings}; false findings {falseFindings}");

if (useModel)
{
    var codex = new CodexCliChatClient(new CodexExecutable(Environment.GetEnvironmentVariable("EXTRACTION_EVAL_CODEX") ?? "codex",
        CodexExecutable.DefaultManagedDirectory()), Environment.GetEnvironmentVariable("EXTRACTION_EVAL_MODEL"),
        enableWebSearch: false);
    var drafter = new ModelLessonDrafter(new Resolver(codex), NullLogger<ModelLessonDrafter>.Instance);
    int drafted = 0, invented = 0;
    foreach (var scenario in file.Drafting)
    {
        var finding = new LessonFinding(scenario.Finding.Tool, scenario.Finding.Incidents, scenario.Finding.Reasons,
            scenario.Finding.ErrorKinds ?? [], now);
        var text = await drafter.DraftAsync(owner, finding, CancellationToken.None);
        var made = (scenario.Forbidden ?? []).Where(part => text?.Contains(part, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (text is not null) drafted++;
        invented += made.Count;
        Console.WriteLine($"{(text is not null && made.Count == 0 ? "pass" : "FAIL")}  drafting  {scenario.Id,-44} " +
                          (text is null ? "no usable lesson" : $"\"{text}\"") + (made.Count > 0 ? $"  INVENTED: {string.Join(", ", made)}" : ""));
    }
    Console.WriteLine($"Lessons drafted {drafted}/{file.Drafting.Length}; invented details {invented}");
    checks.Add(EvalGate.AtLeast("lessons drafted", file.Drafting.Length == 0 ? 1 : (double)drafted / file.Drafting.Length,
        EvalGate.Option(args, "--min-drafted", 0.75)));
    checks.Add(EvalGate.AtMost("invented details", invented, EvalGate.Option(args, "--max-invented", 0)));
}
else
    Console.WriteLine("Drafting skipped (needs a model; pass --model).");

if (!EvalGate.Report(Console.Out, [.. checks])) Environment.ExitCode = 1;

void Score(string area, string id, List<string> notes)
{
    ruleTotal++;
    var ok = notes.Count == 0;
    if (ok) rulePassed++;
    Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {area,-10} {id,-62} {string.Join("; ", notes)}");
}

(List<TurnTraceRecord> Traces, List<LearningSignalRecord> Signals) Build(TraceSeed[]? traceSeeds, SignalSeed[]? signalSeeds)
{
    var traces = (traceSeeds ?? []).Select(seed => new TurnTraceRecord(Id(seed.Id), owner, Id(seed.Conversation), Id(seed.Message),
        null, TurnKinds.Interactive, [], [], seed.Tools.Select(tool => new TurnToolCall(tool.Tool, tool.Outcome, 50)).ToArray(),
        500, "completed", now.AddHours(-seed.HoursAgo), null, seed.Lessons?.Select(Id).ToArray())).ToList();
    var signals = (signalSeeds ?? []).Select(seed => new LearningSignalRecord(Guid.NewGuid(), owner, seed.Kind,
        Id(seed.Conversation ?? "no-conversation"), seed.Message is null ? null : Id(seed.Message), null, seed.Tool, null,
        seed.ErrorKind, now.AddHours(-seed.HoursAgo))).ToList();
    return (traces, signals);
}

static string Name(SelectionScenario scenario, Guid id) =>
    scenario.Lessons.FirstOrDefault(lesson => Id(lesson.Id) == id)?.Id ?? id.ToString("N")[..6];

// A stable id per name, so scenarios can refer to traces, messages and lessons by readable keys.
static Guid Id(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

internal sealed record ScenarioFile(MiningScenario[] Mining, CountingScenario[] Counting, SelectionScenario[] Selection,
    DraftingScenario[] Drafting);
internal sealed record MiningScenario(string Id, TraceSeed[]? Traces, SignalSeed[]? Signals, FindingExpectation[] Expect);
internal sealed record CountingScenario(string Id, string Lesson, TraceSeed[]? Traces, SignalSeed[]? Signals, CountExpectation Expect);
internal sealed record SelectionScenario(string Id, LessonSeed[] Lessons, MessageSeed[] Messages, string[] ExpectShown);
internal sealed record DraftingScenario(string Id, FindingSeed Finding, string[]? Forbidden);
internal sealed record TraceSeed(string Id, string Conversation, string Message, double HoursAgo, ToolSeed[] Tools, string[]? Lessons = null);
internal sealed record ToolSeed(string Tool, string Outcome);
internal sealed record SignalSeed(string Kind, double HoursAgo, string? Message = null, string? Conversation = null,
    string? Tool = null, string? ErrorKind = null);
internal sealed record FindingExpectation(string Tool, int Incidents, string[] Reasons, string[]? ErrorKinds = null);
internal sealed record CountExpectation(int Helpful, int Harmful);
internal sealed record LessonSeed(string Id, string Text, string[] Tools, int Helpful = 0, int Harmful = 0);
internal sealed record MessageSeed(string Role, string? Text = null, string? Call = null);
internal sealed record FindingSeed(string Tool, int Incidents, string[] Reasons, string[]? ErrorKinds = null);

internal sealed class Resolver(IChatClient chat) : IChatClientResolver
{
    public Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose, CancellationToken cancellationToken,
        ModelSettings? overlay = null) => Task.FromResult(chat);
    public Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<EmbeddingModel?>(null);
}
