using Jarvis.Application.Learning;

namespace Jarvis.Application.Usage;

/// <summary>A tool's average duration across its calls in the period.</summary>
public sealed record ToolSpeed(string Tool, int Calls, int AverageMs);

/// <summary>Raw rows for one reporting period, newest first and already capped by the caller.</summary>
public sealed record TurnQualityInputs(
    IReadOnlyList<(int Ms, string Outcome)> ModelCalls,
    long ModelInputTokens,
    long ModelCachedInputTokens,
    IReadOnlyList<(int Ms, string Outcome)> Turns,
    IReadOnlyList<TurnToolCall> ToolCalls,
    int ApprovalsAsked,
    int ApprovalsDenied,
    int AutoApproved);

/// <summary>
/// How smoothly turns ran: how long the model took to answer, how traced turns went (any turn that used memory, a
/// skill or a tool, or did not finish), which tools are slow,
/// and how often the owner was stopped for an approval. <see cref="Flags"/> names what needs attention
/// (<c>slow_replies</c>, <c>failing_turns</c>, <c>approval_friction</c>, <c>low_cache</c>).
/// </summary>
public sealed record TurnQualitySnapshot(
    int ModelCalls,
    int? MedianModelMs,
    int? P90ModelMs,
    double? ModelFailureRate,
    double? CacheHitRate,
    int Turns,
    int? MedianTurnMs,
    int? P90TurnMs,
    int FailedTurns,
    int CancelledTurns,
    int ToolCalls,
    double? ToolFailureRate,
    IReadOnlyList<ToolSpeed> SlowTools,
    int ApprovalsAsked,
    int ApprovalsDenied,
    int AutoApproved,
    IReadOnlyList<string> Flags)
{
    public static TurnQualitySnapshot Empty { get; } =
        TurnQualityMetrics.Compute(new TurnQualityInputs([], 0, 0, [], [], 0, 0, 0));
}

/// <summary>Turns raw rows into the turn quality card. Pure so every rule is easy to test.</summary>
public static class TurnQualityMetrics
{
    public const int MinSamples = 5;
    public const int MinToolCalls = 5;
    public const int MaxSlowTools = 3;
    public const int SlowReplyMs = 30_000;
    public const double FailingTurnRate = 0.1;
    public const int MinApprovalsForFriction = 10;
    public const double LowCacheRate = 0.2;

    public static TurnQualitySnapshot Compute(TurnQualityInputs input)
    {
        var modelDurations = input.ModelCalls
            .Where(call => call.Outcome == UsageOutcomes.Completed && call.Ms > 0)
            .Select(call => call.Ms).ToArray();
        var modelFailed = input.ModelCalls.Count(call => call.Outcome != UsageOutcomes.Completed);
        var turnDurations = input.Turns.Where(turn => turn.Outcome == "completed" && turn.Ms > 0)
            .Select(turn => turn.Ms).ToArray();
        var failedTurns = input.Turns.Count(turn => turn.Outcome == "failed");
        var cancelledTurns = input.Turns.Count(turn => turn.Outcome == "cancelled");
        var toolFailed = input.ToolCalls.Count(call => call.Outcome == ToolOutcomes.Failed);
        var cacheRate = input.ModelInputTokens > 0
            ? Math.Clamp(input.ModelCachedInputTokens / (double)input.ModelInputTokens, 0, 1)
            : (double?)null;

        var p90Model = Percentile(modelDurations, 0.9);
        var flags = new List<string>();
        if (p90Model is > SlowReplyMs) flags.Add("slow_replies");
        if (input.Turns.Count >= MinSamples && failedTurns / (double)input.Turns.Count >= FailingTurnRate)
            flags.Add("failing_turns");
        if (input.ApprovalsAsked >= MinApprovalsForFriction && input.AutoApproved < input.ApprovalsAsked / 2)
            flags.Add("approval_friction");
        if (cacheRate is < LowCacheRate && input.ModelCalls.Count >= MinSamples) flags.Add("low_cache");

        return new TurnQualitySnapshot(
            input.ModelCalls.Count,
            Percentile(modelDurations, 0.5),
            p90Model,
            input.ModelCalls.Count > 0 ? modelFailed / (double)input.ModelCalls.Count : null,
            cacheRate,
            input.Turns.Count,
            Percentile(turnDurations, 0.5),
            Percentile(turnDurations, 0.9),
            failedTurns,
            cancelledTurns,
            input.ToolCalls.Count,
            input.ToolCalls.Count > 0 ? toolFailed / (double)input.ToolCalls.Count : null,
            input.ToolCalls
                .GroupBy(call => call.Tool, StringComparer.Ordinal)
                .Where(group => group.Count() >= MinToolCalls)
                .Select(group => new ToolSpeed(group.Key, group.Count(),
                    (int)Math.Round(group.Average(call => (double)Math.Max(0, call.Ms)))))
                .OrderByDescending(tool => tool.AverageMs).ThenBy(tool => tool.Tool, StringComparer.Ordinal)
                .Take(MaxSlowTools).ToArray(),
            Math.Max(0, input.ApprovalsAsked),
            Math.Max(0, input.ApprovalsDenied),
            Math.Max(0, input.AutoApproved),
            flags);
    }

    /// <summary>Nearest-rank percentile; null until there are enough samples to mean something.</summary>
    internal static int? Percentile(IReadOnlyList<int> values, double fraction)
    {
        if (values.Count < MinSamples) return null;
        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(fraction * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
