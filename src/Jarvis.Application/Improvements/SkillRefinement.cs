using Jarvis.Application.Learning;
using Jarvis.Application.Skills;

namespace Jarvis.Application.Improvements;

public static class SkillRefineRules
{
    public const string FingerprintPrefix = "refine:";

    /// <summary>Each redraft is a model call, so a night redrafts at most this many skills.</summary>
    public const int MaxDraftsPerRun = 2;

    /// <summary>One proposal per skill version: a refusal holds until the text changes.</summary>
    public static string Fingerprint(SkillRecord skill) => $"{FingerprintPrefix}{skill.Name}:v{skill.Version}";
}

/// <summary>
/// What went wrong in the runs of one skill version, as names and counts only: how many went badly, which tools failed
/// in them and with which kinds of error. No message text.
/// </summary>
public sealed record SkillRefineEvidence(int Runs, int Successes, int BadlyRated,
    IReadOnlyList<KeyValuePair<string, int>> FailedTools, IReadOnlyList<string> ErrorKinds);

/// <summary>What accepting a refinement saves. It applies only while the skill is still at <see cref="BaseVersion"/>.</summary>
public sealed record SkillRefinePayload(string Name, string Description, string Instructions, int BaseVersion);

/// <summary>Redrafts a skill that mostly goes badly. Returns null if nothing usable came back.</summary>
public interface ISkillRefiner
{
    Task<SkillDraft?> RefineAsync(Guid ownerId, SkillRecord skill, SkillRefineEvidence evidence,
        CancellationToken cancellationToken);
}

public static class SkillRefineEngine
{
    /// <summary>Skills worth redrafting: not locked (the agent may not change those) and mostly going badly.</summary>
    public static IReadOnlyList<SkillRecord> Candidates(IEnumerable<SkillRecord> skills) =>
        [.. skills.Where(skill => skill.Status == SkillStatuses.Active && !skill.IsLocked &&
                                  SkillOutcomeRules.NeedsRefining(skill))
            .OrderBy(skill => SkillOutcomeRules.Wilson(skill.OutcomeSuccesses, skill.OutcomeRuns))
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)];

    public static SkillRefineEvidence Collect(SkillRecord skill, IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals)
    {
        var since = skill.Version > 1 ? skill.UpdatedAt : DateTimeOffset.MinValue;
        var runs = traces.Where(trace => trace.CreatedAt > since && trace.Skills.Contains(skill.Name, StringComparer.Ordinal))
            .ToList();
        var feedback = TurnFeedback.From(traces, signals);
        var failed = runs.SelectMany(trace => trace.Tools.Where(call => call.Outcome == ToolOutcomes.Failed))
            .GroupBy(call => call.Tool, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(5).ToArray();
        var failedTools = failed.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var errorKinds = signals.Where(signal => signal.Kind == LearningSignalKinds.ToolFailure &&
                                                 signal.Tool is not null && signal.ErrorKind is not null &&
                                                 failedTools.Contains(signal.Tool))
            .Select(signal => signal.ErrorKind!).Distinct().Order().ToArray();
        return new SkillRefineEvidence(skill.OutcomeRuns, skill.OutcomeSuccesses,
            runs.Count(trace => feedback.Bad.Contains(trace.Id)), failed, errorKinds);
    }
}
