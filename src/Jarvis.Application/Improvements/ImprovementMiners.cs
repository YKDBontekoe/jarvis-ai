using Jarvis.Application.Learning;

namespace Jarvis.Application.Improvements;

/// <summary>A run of tools Jarvis keeps using in the same order, and how often.</summary>
public sealed record ToolSequenceFinding(string Key, IReadOnlyList<string> Tools, int Runs, int Conversations,
    DateTimeOffset LastSeen)
{
    public string Fingerprint => SkillMinerEngine.FingerprintPrefix + Key;
}

/// <summary>
/// Finds tool sequences that worked and keep coming back, which is the shape of a skill worth saving. Pure and
/// deterministic: it looks only at tool names and outcomes from run traces, never at text.
/// </summary>
public static class SkillMinerEngine
{
    public const string FingerprintPrefix = "seq:";
    public const int WindowDays = 30;
    public const int MinLength = 2;
    public const int MaxLength = 5;
    public const int MinRuns = 3;
    public const int MinConversations = 2;
    public const int MaxFindings = 5;

    public static IReadOnlyList<ToolSequenceFinding> Mine(IReadOnlyList<TurnTraceRecord> traces, DateTimeOffset now)
    {
        var since = now.AddDays(-WindowDays);
        var runs = new Dictionary<string, (List<string> Tools, HashSet<Guid> Traces, HashSet<Guid> Conversations,
            DateTimeOffset LastSeen)>(StringComparer.Ordinal);
        foreach (var trace in traces)
        {
            if (trace.CreatedAt < since || trace.CreatedAt > now || trace.Outcome != "completed") continue;
            var tools = Collapse(trace.Tools);
            for (var length = MinLength; length <= Math.Min(MaxLength, tools.Count); length++)
            for (var start = 0; start + length <= tools.Count; start++)
            {
                var window = tools.GetRange(start, length);
                var key = string.Join('>', window);
                if (!runs.TryGetValue(key, out var entry))
                    entry = (window, [], [], trace.CreatedAt);
                entry.Traces.Add(trace.Id);
                entry.Conversations.Add(trace.ConversationId);
                runs[key] = (entry.Tools, entry.Traces, entry.Conversations,
                    trace.CreatedAt > entry.LastSeen ? trace.CreatedAt : entry.LastSeen);
            }
        }

        var found = runs
            .Where(pair => pair.Value.Traces.Count >= MinRuns && pair.Value.Conversations.Count >= MinConversations)
            .Select(pair => new ToolSequenceFinding(pair.Key, pair.Value.Tools, pair.Value.Traces.Count,
                pair.Value.Conversations.Count, pair.Value.LastSeen))
            .ToList();
        // A sequence that only ever appears inside a longer one adds nothing: keep the longer.
        var kept = found.Where(candidate => !found.Any(other =>
                other.Tools.Count > candidate.Tools.Count && other.Runs >= candidate.Runs
                && Contains(other.Tools, candidate.Tools)))
            .OrderByDescending(item => item.Runs).ThenByDescending(item => item.Tools.Count)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(MaxFindings)
            .ToList();
        return kept;
    }

    public static double Confidence(ToolSequenceFinding finding) => Math.Min(0.95, 0.5 + 0.05 * finding.Runs);

    /// <summary>Successful calls only, in order, with immediate repeats folded and skill management left out.</summary>
    private static List<string> Collapse(IReadOnlyList<TurnToolCall> calls)
    {
        var tools = new List<string>();
        foreach (var call in calls)
        {
            if (call.Outcome != ToolOutcomes.Completed || IsSkillTool(call.Tool)) continue;
            if (tools.Count == 0 || !string.Equals(tools[^1], call.Tool, StringComparison.Ordinal))
                tools.Add(call.Tool);
        }

        return tools;
    }

    private static bool IsSkillTool(string tool) => tool.Contains("skill", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(IReadOnlyList<string> outer, IReadOnlyList<string> inner)
    {
        for (var start = 0; start + inner.Count <= outer.Count; start++)
            if (!inner.Where((tool, offset) => outer[start + offset] != tool).Any())
                return true;
        return false;
    }
}

/// <summary>Something that goes wrong more than it should: a skill or memory behind bad replies, or a failing tool.</summary>
public sealed record ReviewFinding(string Kind, string Target, int Count, int Total, int Positive, DateTimeOffset LastSeen)
{
    public const string SkillKind = "skill";
    public const string MemoryKind = "memory";
    public const string ToolKind = "tool";
}

/// <summary>
/// Looks for things that keep showing up when a reply was rated badly, and for tools that mostly fail. Evidence only:
/// nothing here changes ranking, and acting on a finding is the owner's call.
/// </summary>
public static class ReviewMinerEngine
{
    public const string FingerprintPrefix = "review:";
    public const int MinThumbsDown = 3;
    public const int MaxThumbsUp = 1;
    public const int ToolWindowDays = 7;
    public const int MinToolCalls = 5;
    public const double MinFailureRate = 0.5;
    public const int MaxFindings = 6;

    public static IReadOnlyList<ReviewFinding> Mine(IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now, DateTimeOffset ratingsSince)
    {
        var found = new List<ReviewFinding>();
        found.AddRange(MineRatings(traces, signals, now, ratingsSince));
        found.AddRange(MineToolFailures(traces, now));
        return found.OrderByDescending(item => item.Count).ThenBy(item => item.Target, StringComparer.Ordinal)
            .Take(MaxFindings).ToList();
    }

    public static string Fingerprint(ReviewFinding finding, DateTimeOffset now) => finding.Kind switch
    {
        // A closed tool report returns after a month, since a tool can break again.
        ReviewFinding.ToolKind => $"{FingerprintPrefix}tool:{finding.Target}:{now:yyyy-MM}",
        ReviewFinding.SkillKind => $"{FingerprintPrefix}skill:{finding.Target}",
        _ => $"{FingerprintPrefix}memory:{finding.Target}"
    };

    private static IEnumerable<ReviewFinding> MineRatings(IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now, DateTimeOffset since)
    {
        var rating = new Dictionary<Guid, string>();
        foreach (var signal in signals.Where(s => s.MessageId is not null && s.CreatedAt >= since && s.CreatedAt <= now
                     && s.Kind is LearningSignalKinds.ThumbsDown or LearningSignalKinds.ThumbsUp)
                     .OrderBy(s => s.CreatedAt))
            rating[signal.MessageId!.Value] = signal.Kind; // the last thumb on a reply wins
        var counts = new Dictionary<(string Kind, string Target), (int Down, int Up, DateTimeOffset Last)>();
        foreach (var trace in traces)
        {
            if (trace.MessageId is not { } messageId || !rating.TryGetValue(messageId, out var kind)) continue;
            var targets = trace.Skills.Distinct(StringComparer.Ordinal)
                .Select(skill => (ReviewFinding.SkillKind, skill))
                .Concat(trace.MemoryIds.Distinct().Select(id => (ReviewFinding.MemoryKind, id.ToString("N"))));
            foreach (var target in targets)
            {
                counts.TryGetValue(target, out var entry);
                counts[target] = (entry.Down + (kind == LearningSignalKinds.ThumbsDown ? 1 : 0),
                    entry.Up + (kind == LearningSignalKinds.ThumbsUp ? 1 : 0),
                    trace.CreatedAt > entry.Last ? trace.CreatedAt : entry.Last);
            }
        }

        return counts.Where(pair => pair.Value.Down >= MinThumbsDown && pair.Value.Up <= MaxThumbsUp)
            .Select(pair => new ReviewFinding(pair.Key.Kind, pair.Key.Target, pair.Value.Down,
                pair.Value.Down + pair.Value.Up, pair.Value.Up, pair.Value.Last));
    }

    private static IEnumerable<ReviewFinding> MineToolFailures(IReadOnlyList<TurnTraceRecord> traces, DateTimeOffset now)
    {
        var since = now.AddDays(-ToolWindowDays);
        var calls = traces.Where(trace => trace.CreatedAt >= since && trace.CreatedAt <= now)
            .SelectMany(trace => trace.Tools.Select(call => (call, trace.CreatedAt)))
            .GroupBy(item => item.call.Tool, StringComparer.Ordinal);
        foreach (var group in calls)
        {
            var total = group.Count();
            var failed = group.Count(item => item.call.Outcome == ToolOutcomes.Failed);
            if (total >= MinToolCalls && failed >= MinToolCalls && failed / (double)total > MinFailureRate)
                yield return new ReviewFinding(ReviewFinding.ToolKind, group.Key, failed, total, 0,
                    group.Max(item => item.CreatedAt));
        }
    }
}
