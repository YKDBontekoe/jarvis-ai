using System.Text.RegularExpressions;

namespace Jarvis.Application.Learning;

/// <summary>
/// What Jarvis can learn from how a reply was received, besides what the owner said. Signals carry ids, tool names and
/// counts, never message text, notes, excerpts or tool arguments.
/// </summary>
public static class LearningSignalKinds
{
    public const string Regenerate = "regenerate";
    public const string ApprovalDenied = "approval_denied";
    public const string ThumbsUp = "thumbs_up";
    public const string ThumbsDown = "thumbs_down";
    public const string Correction = "correction";
    public const string ToolFailure = "tool_failure";

    public static readonly IReadOnlyList<string> All =
        [Regenerate, ApprovalDenied, ThumbsUp, ThumbsDown, Correction, ToolFailure];
}

public static class TurnKinds
{
    public const string Interactive = "interactive";
    public const string Task = "task";
}

public static class ToolOutcomes
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>
/// Model calls and tokens one run spent on the conversation itself (not background work): every tool step is a call, so
/// the totals show what a turn cost and how much of its prompt the provider served from cache.
/// </summary>
public sealed record TurnUsage(int ModelCalls, long InputTokens, long CachedInputTokens, long OutputTokens)
{
    public static TurnUsage Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Share of input tokens served from the provider's cache, or null when nothing was read.</summary>
    public double? CacheHitRatio => InputTokens > 0 ? Math.Min(1.0, (double)CachedInputTokens / InputTokens) : null;
}

/// <summary>One tool call in a turn: its name, whether it worked, and how long it took.</summary>
public sealed record TurnToolCall(string Tool, string Outcome, int Ms);

/// <summary>
/// What one run did, kept so later passes can tell which memories, skills and tools went into a reply. The assistant
/// message id is missing when the run paused for an approval without saying anything.
/// </summary>
public sealed record TurnTraceDraft(
    Guid ConversationId,
    Guid? MessageId,
    string Kind,
    IReadOnlyList<Guid> MemoryIds,
    IReadOnlyList<string> Skills,
    IReadOnlyList<TurnToolCall> Tools,
    int TotalMs,
    string Outcome,
    TurnUsage? Usage = null,
    IReadOnlyList<Guid>? LessonIds = null)
{
    /// <summary>A plain reply that used nothing is not worth a row.</summary>
    public bool IsWorthKeeping =>
        MemoryIds.Count > 0 || Skills.Count > 0 || Tools.Count > 0 || Outcome != "completed" ||
        LessonIds is { Count: > 0 };
}

public sealed record LearningSignalRecord(Guid Id, Guid OwnerId, string Kind, Guid ConversationId, Guid? MessageId,
    Guid? ProfileId, string? Tool, string? Category, string? ErrorKind, DateTimeOffset CreatedAt);

public sealed record TurnTraceRecord(Guid Id, Guid OwnerId, Guid ConversationId, Guid? MessageId, Guid? ProfileId,
    string Kind, IReadOnlyList<Guid> MemoryIds, IReadOnlyList<string> Skills, IReadOnlyList<TurnToolCall> Tools,
    int TotalMs, string Outcome, DateTimeOffset CreatedAt, TurnUsage? Usage = null,
    IReadOnlyList<Guid>? LessonIdList = null)
{
    /// <summary>The lessons shown to the model in this run.</summary>
    public IReadOnlyList<Guid> LessonIds => LessonIdList ?? [];
}

/// <summary>
/// Records learning signals and run traces without ever slowing or failing the turn that produced them. Calls return at
/// once; nothing is written when the conversation's assistant profile does not contribute to learning or the owner
/// switched capture off.
/// </summary>
public interface ILearningRecorder
{
    void RecordSignal(Guid ownerId, Guid conversationId, string kind, Guid? messageId = null, string? tool = null,
        string? category = null, string? errorKind = null);

    void RecordTrace(Guid ownerId, TurnTraceDraft trace);
}

/// <summary>
/// Collects, for the one turn running in this scope, which memories reached the model and which skills it loaded. The
/// run coordinator reads it when the run ends.
/// </summary>
public interface ITurnTraceCollector
{
    void MemoryInjected(Guid memoryId);
    void SkillLoaded(string name);
    void LessonInjected(Guid proposalId);
    IReadOnlyList<Guid> Lessons();

    /// <summary>Adds the tokens of one finished chat model call to this turn.</summary>
    void ModelCallFinished(long inputTokens, long cachedInputTokens, long outputTokens);

    (IReadOnlyList<Guid> Memories, IReadOnlyList<string> Skills) Snapshot();
    TurnUsage Usage();
}

public sealed class TurnTraceCollector : ITurnTraceCollector
{
    private readonly object _gate = new();
    private readonly List<Guid> _memories = [];
    private readonly List<string> _skills = [];
    private readonly List<Guid> _lessons = [];
    private TurnUsage _usage = TurnUsage.Empty;

    public void LessonInjected(Guid proposalId)
    {
        lock (_gate)
            if (!_lessons.Contains(proposalId)) _lessons.Add(proposalId);
    }

    public IReadOnlyList<Guid> Lessons()
    {
        lock (_gate) return [.. _lessons];
    }

    public void ModelCallFinished(long inputTokens, long cachedInputTokens, long outputTokens)
    {
        lock (_gate)
            _usage = new TurnUsage(_usage.ModelCalls + 1, _usage.InputTokens + Math.Max(0, inputTokens),
                _usage.CachedInputTokens + Math.Max(0, cachedInputTokens), _usage.OutputTokens + Math.Max(0, outputTokens));
    }

    public TurnUsage Usage()
    {
        lock (_gate) return _usage;
    }

    public void MemoryInjected(Guid memoryId)
    {
        lock (_gate)
            if (!_memories.Contains(memoryId)) _memories.Add(memoryId);
    }

    public void SkillLoaded(string name)
    {
        lock (_gate)
            if (!_skills.Contains(name, StringComparer.Ordinal)) _skills.Add(name);
    }

    public (IReadOnlyList<Guid> Memories, IReadOnlyList<string> Skills) Snapshot()
    {
        lock (_gate) return ([.. _memories], [.. _skills]);
    }
}

public interface ILearningStore
{
    Task AddSignalAsync(LearningSignalRecord signal, CancellationToken cancellationToken);
    Task AddTraceAsync(TurnTraceRecord trace, CancellationToken cancellationToken);
    Task<IReadOnlyList<LearningSignalRecord>> ListSignalsAsync(Guid ownerId, DateTimeOffset since,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TurnTraceRecord>> ListTracesAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken);

    /// <summary>Deletes traces older than <paramref name="tracesBefore"/> and signals older than <paramref name="signalsBefore"/>.</summary>
    Task<int> PruneAsync(Guid ownerId, DateTimeOffset tracesBefore, DateTimeOffset signalsBefore,
        CancellationToken cancellationToken);
}

/// <summary>
/// Spots a user message that pushes back on the reply before it ("no, shorter", "I said ..."). It looks only at the
/// start of the message, never calls a model, and its result is stored as a bare signal with no text.
/// </summary>
public static partial class CorrectionDetector
{
    public const int MaxLength = 400;

    public static bool LooksLikeCorrection(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var text = message.Trim();
        // A long message is a new request that happens to start with "no"; corrections are short.
        if (text.Length > MaxLength) return false;
        return Opening().IsMatch(text);
    }

    [GeneratedRegex(
        @"^\W*(no[\s,.!:;-]|nope\b|nee\b|nein\b|that'?s (not|wrong|incorrect)|that is (not|wrong|incorrect)|not (like|what|that|quite)|wrong\b|incorrect\b|i (said|meant|asked)\b|i didn'?t (say|ask|mean)\b|don'?t\b|do not\b|stop\b|never\b|instead\b|actually[,\s]|please don'?t\b|try again\b|that'?s not what\b|je bedoelt\b|niet zo\b|dat klopt niet\b|ik zei\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Opening();
}
