namespace Jarvis.Application.Usage;

public sealed record ToolReliability(string Tool, int Calls, int Failed, double FailureRate);

public sealed record ProposalCounts(int Pending, int Accepted, int Applied, int Dismissed, int Undone)
{
    public static ProposalCounts None { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>Raw counts for one reporting period and the period right before it.</summary>
public sealed record ImprovementInputs(
    int ThumbsUp,
    int ThumbsDown,
    int PreviousThumbsUp,
    int PreviousThumbsDown,
    int Regenerates,
    int AssistantReplies,
    int Denials,
    IReadOnlyList<ToolReliability> Tools,
    int SkillsLearned,
    ProposalCounts Proposals);

/// <summary>
/// How Jarvis is doing at getting better for this owner. <see cref="Trend"/> is <c>better</c>, <c>steady</c> or
/// <c>worse</c> and is null until both periods have enough ratings to compare.
/// </summary>
public sealed record ImprovementSnapshot(
    int ThumbsUp,
    int ThumbsDown,
    int PreviousThumbsUp,
    int PreviousThumbsDown,
    string? Trend,
    int Regenerates,
    double? RegenerateRate,
    int Denials,
    IReadOnlyList<ToolReliability> WeakTools,
    int SkillsLearned,
    ProposalCounts Proposals,
    double? AcceptanceRate)
{
    public static ImprovementSnapshot Empty { get; } = ImprovementMetrics.Compute(
        new ImprovementInputs(0, 0, 0, 0, 0, 0, 0, [], 0, ProposalCounts.None));
}

/// <summary>Turns raw counts into the improvement card. Pure so every rule is easy to test.</summary>
public static class ImprovementMetrics
{
    public const int MinRatingsForTrend = 3;
    public const double TrendMargin = 0.1;
    public const int MinToolCalls = 5;
    public const int MaxWeakTools = 3;

    public static ImprovementSnapshot Compute(ImprovementInputs input)
    {
        var up = Math.Max(0, input.ThumbsUp);
        var down = Math.Max(0, input.ThumbsDown);
        var previousUp = Math.Max(0, input.PreviousThumbsUp);
        var previousDown = Math.Max(0, input.PreviousThumbsDown);
        var replies = Math.Max(0, input.AssistantReplies);
        var regenerates = Math.Max(0, input.Regenerates);
        var proposals = input.Proposals;
        var decided = proposals.Accepted + proposals.Applied + proposals.Dismissed + proposals.Undone;
        return new ImprovementSnapshot(
            up, down, previousUp, previousDown, Trend(up, down, previousUp, previousDown),
            regenerates, replies > 0 ? Math.Min(1d, regenerates / (double)replies) : null,
            Math.Max(0, input.Denials),
            input.Tools.Where(tool => tool.Calls >= MinToolCalls && tool.Failed > 0)
                .Select(tool => tool with { FailureRate = tool.Failed / (double)tool.Calls })
                .OrderByDescending(tool => tool.FailureRate).ThenByDescending(tool => tool.Calls)
                .ThenBy(tool => tool.Tool, StringComparer.Ordinal)
                .Take(MaxWeakTools).ToArray(),
            Math.Max(0, input.SkillsLearned), proposals,
            decided > 0 ? (proposals.Accepted + proposals.Applied) / (double)decided : null);
    }

    private static string? Trend(int up, int down, int previousUp, int previousDown)
    {
        if (up + down < MinRatingsForTrend || previousUp + previousDown < MinRatingsForTrend) return null;
        var change = up / (double)(up + down) - previousUp / (double)(previousUp + previousDown);
        return change >= TrendMargin ? "better" : change <= -TrendMargin ? "worse" : "steady";
    }
}
