using Jarvis.Application.Learning;

namespace Jarvis.Application.Skills;

/// <summary>Runs and successes to add to a skill's current version, counted through the given moment.</summary>
public sealed record SkillOutcomeDelta(int Runs, int Successes, DateTimeOffset CountedThrough);

public static class SkillOutcomeRules
{
    /// <summary>Ratings land soon after a reply, so only turns older than this are counted.</summary>
    public static readonly TimeSpan CountingDelay = TimeSpan.FromHours(6);

    /// <summary>Below this many runs a skill keeps its place in the list: too little to tell.</summary>
    public const int MinRunsToRank = 3;

    /// <summary>Where a skill with too few runs sits between the poor and the proven ones.</summary>
    public const double NeutralScore = 0.35;

    /// <summary>A skill is worth refining at this many runs or more, with at most this share of them going well.</summary>
    public const int MinRunsToRefine = 3;
    public const double MaxSuccessShareToRefine = 0.6;

    /// <summary>Lower end of the 95% Wilson interval for the share of runs that went well.</summary>
    public static double Wilson(int successes, int runs)
    {
        if (runs <= 0) return 0;
        const double z = 1.96;
        var n = (double)runs;
        var p = Math.Clamp(successes, 0, runs) / n;
        var centre = p + z * z / (2 * n);
        var margin = z * Math.Sqrt((p * (1 - p) + z * z / (4 * n)) / n);
        return Math.Max(0, (centre - margin) / (1 + z * z / n));
    }

    public static double Score(SkillRecord skill) =>
        skill.OutcomeRuns >= MinRunsToRank ? Wilson(skill.OutcomeSuccesses, skill.OutcomeRuns) : NeutralScore;

    public static bool NeedsRefining(SkillRecord skill) =>
        skill.OutcomeRuns >= MinRunsToRefine &&
        skill.OutcomeSuccesses <= skill.OutcomeRuns * MaxSuccessShareToRefine;
}

/// <summary>
/// Counts how the turns that loaded a skill went. A run goes well when it finished, none of its tools failed, and the
/// owner did not rate it down, ask for it again or correct it. Counts belong to the skill's current version: they reset
/// when its text changes, and a turn is counted once.
/// </summary>
public static class SkillOutcomeEngine
{
    public static IReadOnlyDictionary<Guid, SkillOutcomeDelta> Tally(IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, IReadOnlyList<SkillRecord> skills, DateTimeOffset now)
    {
        var through = now - SkillOutcomeRules.CountingDelay;
        var feedback = TurnFeedback.From(traces, signals);
        var result = new Dictionary<Guid, SkillOutcomeDelta>();
        foreach (var skill in skills)
        {
            // A skill that has been rewritten but never counted starts from its last change, not from its first trace.
            var after = skill.OutcomeCountedAt ?? (skill.Version > 1 ? skill.UpdatedAt : DateTimeOffset.MinValue);
            int runs = 0, successes = 0;
            foreach (var trace in traces.Where(trace => trace.CreatedAt > after && trace.CreatedAt <= through &&
                                                        trace.Skills.Contains(skill.Name, StringComparer.Ordinal)))
            {
                runs++;
                if (trace.Outcome == "completed" && !trace.Tools.Any(call => call.Outcome == ToolOutcomes.Failed) &&
                    !feedback.Bad.Contains(trace.Id))
                    successes++;
            }

            if (runs > 0) result[skill.Id] = new SkillOutcomeDelta(runs, successes, through);
        }

        return result;
    }
}
