using System.Text.Json;
using Jarvis.Application.Proactivity;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var scenarios = JsonSerializer.Deserialize<Scenario[]>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scenarios.json")), json)!;
var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
var results = new List<object>();
var failures = 0;
foreach (var scenario in scenarios)
{
    var sources = scenario.Sources ?? [];
    var policy = new ProactivePolicy(TimeZoneId: scenario.TimeZoneId ?? "UTC");
    var value = new ProactiveCase(Guid.NewGuid(), Guid.NewGuid(), "eval", scenario.Kind, scenario.Id,
        "Evaluate synthetic work", "open", true, null, null, scenario.SourceRefs ?? sources.Select(s => s.Ref).ToArray(),
        null, policy, scenario.Steps ?? [], scenario.Assumptions ?? [], scenario.Deadline, scenario.AvailableMinutes,
        "Review", sources, [], [], [], now.AddDays(-1), now, null, now);
    var evaluation = ProactiveRules.Evaluate(value, sources, now);
    var errors = new List<string>();
    if (scenario.ExpectedNext is { } next && !evaluation.NextAction.Contains(next, StringComparison.OrdinalIgnoreCase)) errors.Add("next step");
    if (scenario.ExpectedFinding is { } finding && !evaluation.Findings.Any(f => f.Key == finding)) errors.Add("missing intervention");
    if (scenario.ExpectedReason is { } reason && !evaluation.Findings.Any(f => f.Reason.Contains(reason))) errors.Add("missing arithmetic");
    if (scenario.ForbiddenFinding is { } forbidden && evaluation.Findings.Any(f => f.Key == forbidden)) errors.Add("unnecessary intervention");
    if (scenario.UnfinishedStep is { } step && evaluation.Steps.Any(s => s.Id == step && s.Done)) errors.Add("false completion");
    if (scenario.QuietAt is { } at && ProactiveRules.Quiet(policy, at) != scenario.ExpectedQuiet) errors.Add("quiet hours");
    if (errors.Count > 0) failures++;
    results.Add(new { scenario.Id, scenario.Category, passed = errors.Count == 0, errors, evaluation.NextAction, evaluation.Findings });
    Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {scenario.Id}: {string.Join(", ", errors)}");
}
Console.WriteLine($"Synthetic scenarios: {scenarios.Length - failures}/{scenarios.Length}. Explicit owner usefulness: unknown; silence is not feedback.");
if (args.Length == 2 && args[0] == "--output")
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new { dataset = "proactive-v1", kind = "deterministic rules, no model or external writes",
        successfulScenarios = scenarios.Length - failures, total = scenarios.Length, explicitOwnerUsefulness = (double?)null, results }, json));
return failures == 0 ? 0 : 1;

internal sealed record Scenario(string Id, string Category, string Kind,
    IReadOnlyList<ProactiveStep>? Steps = null, IReadOnlyList<ProactiveSource>? Sources = null,
    IReadOnlyList<string>? SourceRefs = null, IReadOnlyList<ProactiveAssumption>? Assumptions = null,
    DateTimeOffset? Deadline = null, int? AvailableMinutes = null, string? ExpectedNext = null,
    string? ExpectedFinding = null, string? ExpectedReason = null, string? ForbiddenFinding = null,
    string? UnfinishedStep = null, DateTimeOffset? QuietAt = null, bool? ExpectedQuiet = null, string? TimeZoneId = null);
