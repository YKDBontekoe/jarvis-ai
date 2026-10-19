using Jarvis.Application.Learning;

namespace Jarvis.Application.Improvements;

public static class LessonRules
{
    public const string FingerprintPrefix = "lesson:";
    public const int WindowDays = 7;
    public const int MinIncidents = 2;
    public const int MaxFindings = 6;

    /// <summary>Each draft is a model call, so a night drafts at most this many.</summary>
    public const int MaxDraftsPerRun = 3;

    public const int MaxTextLength = 240;

    /// <summary>Three incidents reach this; fewer wait for the owner. Compared after rounding to two places.</summary>
    public const double AutoApplyMinConfidence = 0.8;
    public const int MaxInForce = 20;

    /// <summary>Switched off once it was shown in this many badly rated turns and in more of them than well rated ones.</summary>
    public const int DisableAtHarmful = 3;

    public static readonly TimeSpan RetireAfter = TimeSpan.FromDays(60);

    /// <summary>Ratings land soon after a reply, so only turns older than this are counted.</summary>
    public static readonly TimeSpan CountingDelay = TimeSpan.FromHours(6);

    /// <summary>A lesson closed by the owner or switched off returns after a month, since the tool can change again.</summary>
    public static string Fingerprint(string tool, DateTimeOffset now) => $"{FingerprintPrefix}tool:{tool}:{now:yyyy-MM}";
}

public static class LessonReasons
{
    public const string ToolFailed = "tool_failed";
    public const string Corrected = "corrected";
    public const string Regenerated = "regenerated";
    public const string ThumbsDown = "thumbs_down";
    public const string ApprovalDenied = "approval_denied";
}

/// <summary>A tool that was part of several turns that went wrong, and how they went wrong. Names and counts only.</summary>
public sealed record LessonFinding(string Tool, int Incidents, IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ErrorKinds, DateTimeOffset LastSeen);

/// <summary>Drafts one short lesson from a finding. Returns null if nothing usable came back.</summary>
public interface ILessonDrafter
{
    Task<string?> DraftAsync(Guid ownerId, LessonFinding finding, CancellationToken cancellationToken);
}

/// <summary>
/// Finds tools that keep being part of turns that went wrong: the tool failed, the owner corrected the reply, asked for
/// it again, rated it down or declined its approval. It reads ids, tool names, signal kinds and error kinds only.
/// </summary>
public static class LessonMinerEngine
{
    public static IReadOnlyList<LessonFinding> Mine(IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now)
    {
        var since = now.AddDays(-LessonRules.WindowDays);
        var recent = traces.Where(trace => trace.CreatedAt >= since && trace.CreatedAt <= now).ToList();
        var window = signals.Where(signal => signal.CreatedAt >= since && signal.CreatedAt <= now).ToList();
        var incidents = new Dictionary<string, Dictionary<Guid, (HashSet<string> Reasons, DateTimeOffset At)>>(StringComparer.Ordinal);

        void Add(string tool, Guid key, string reason, DateTimeOffset at)
        {
            if (string.IsNullOrWhiteSpace(tool)) return;
            if (!incidents.TryGetValue(tool, out var byKey)) incidents[tool] = byKey = [];
            if (!byKey.TryGetValue(key, out var entry)) byKey[key] = entry = ([], at);
            entry.Reasons.Add(reason);
            if (at > entry.At) byKey[key] = (entry.Reasons, at);
        }

        foreach (var trace in recent)
            foreach (var call in trace.Tools.Where(call => call.Outcome == ToolOutcomes.Failed))
                Add(call.Tool, trace.Id, LessonReasons.ToolFailed, trace.CreatedAt);

        // The last thumb on a reply wins.
        var thumbs = new Dictionary<Guid, string>();
        foreach (var signal in window.Where(s => s.MessageId is not null &&
                                                 s.Kind is LearningSignalKinds.ThumbsDown or LearningSignalKinds.ThumbsUp)
                     .OrderBy(s => s.CreatedAt))
            thumbs[signal.MessageId!.Value] = signal.Kind;
        var regenerated = window.Where(s => s.Kind == LearningSignalKinds.Regenerate && s.MessageId is not null)
            .Select(s => s.MessageId!.Value).ToHashSet();
        foreach (var trace in recent.Where(trace => trace.MessageId is not null))
        {
            var messageId = trace.MessageId!.Value;
            if (thumbs.TryGetValue(messageId, out var thumb) && thumb == LearningSignalKinds.ThumbsDown)
                AddTurn(trace, LessonReasons.ThumbsDown);
            if (regenerated.Contains(messageId)) AddTurn(trace, LessonReasons.Regenerated);
        }

        // A correction is stored with the user's own message, so it belongs to the reply just before it.
        foreach (var correction in window.Where(s => s.Kind == LearningSignalKinds.Correction))
        {
            var reply = recent.Where(trace => trace.ConversationId == correction.ConversationId &&
                                              trace.CreatedAt <= correction.CreatedAt)
                .OrderByDescending(trace => trace.CreatedAt).FirstOrDefault();
            if (reply is not null) AddTurn(reply, LessonReasons.Corrected);
        }

        foreach (var denied in window.Where(s => s.Kind == LearningSignalKinds.ApprovalDenied && s.Tool is not null))
            Add(denied.Tool!, denied.Id, LessonReasons.ApprovalDenied, denied.CreatedAt);

        var errorKinds = window.Where(s => s.Kind == LearningSignalKinds.ToolFailure && s.Tool is not null && s.ErrorKind is not null)
            .GroupBy(s => s.Tool!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(s => s.ErrorKind!).Distinct().Order().ToArray(),
                StringComparer.Ordinal);

        return incidents
            .Where(pair => pair.Value.Count >= LessonRules.MinIncidents)
            .Select(pair => new LessonFinding(pair.Key, pair.Value.Count,
                pair.Value.Values.SelectMany(entry => entry.Reasons).Distinct().Order().ToArray(),
                errorKinds.GetValueOrDefault(pair.Key, []), pair.Value.Values.Max(entry => entry.At)))
            .OrderByDescending(finding => finding.Incidents).ThenBy(finding => finding.Tool, StringComparer.Ordinal)
            .Take(LessonRules.MaxFindings).ToList();

        void AddTurn(TurnTraceRecord trace, string reason)
        {
            foreach (var tool in trace.Tools.Select(call => call.Tool).Distinct(StringComparer.Ordinal))
                Add(tool, trace.Id, reason, trace.CreatedAt);
        }
    }

    public static double Confidence(LessonFinding finding) => Math.Min(0.9, 0.5 + 0.1 * finding.Incidents);
}

/// <summary>How many rated turns a lesson was shown in, for and against it, since the last count.</summary>
public sealed record LessonTally(int Helpful, int Harmful, DateTimeOffset? LastInjectedAt, DateTimeOffset CountedThrough);

/// <summary>
/// Credits a lesson with the turns it was shown in that the owner rated up, and blames it for turns rated down, asked
/// for again or corrected. Unrated turns count for neither; a tool failing in a turn is not held against the lesson.
/// </summary>
public static class LessonCounterEngine
{
    public static IReadOnlyDictionary<Guid, LessonTally> Tally(IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, IReadOnlyDictionary<Guid, LessonPayload> inForce, DateTimeOffset now)
    {
        var through = now - LessonRules.CountingDelay;
        var feedback = TurnFeedback.From(traces, signals);

        var result = new Dictionary<Guid, LessonTally>();
        foreach (var (id, payload) in inForce)
        {
            int helpful = 0, harmful = 0;
            DateTimeOffset? lastInjected = payload.LastInjectedAt;
            foreach (var trace in traces.Where(trace => trace.LessonIds.Contains(id)))
            {
                if (lastInjected is null || trace.CreatedAt > lastInjected) lastInjected = trace.CreatedAt;
                if (trace.CreatedAt > through || trace.CreatedAt <= (payload.CountedThrough ?? DateTimeOffset.MinValue))
                    continue;
                if (feedback.Bad.Contains(trace.Id)) harmful++;
                else if (feedback.Good.Contains(trace.Id)) helpful++;
            }

            result[id] = new LessonTally(helpful, harmful, lastInjected, through);
        }

        return result;
    }
}
