using Jarvis.Application.Learning;
using Jarvis.Application.Usage;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class TurnQualityMetricsTests
{
    [Fact]
    public void Nothing_recorded_gives_an_empty_card_without_division_errors()
    {
        var snapshot = TurnQualitySnapshot.Empty;

        Assert.Equal(0, snapshot.ModelCalls);
        Assert.Null(snapshot.MedianModelMs);
        Assert.Null(snapshot.ModelFailureRate);
        Assert.Null(snapshot.CacheHitRate);
        Assert.Null(snapshot.ToolFailureRate);
        Assert.Empty(snapshot.SlowTools);
        Assert.Empty(snapshot.Flags);
    }

    [Fact]
    public void Reply_times_use_completed_calls_and_need_enough_samples()
    {
        var few = TurnQualityMetrics.Compute(Inputs(modelCalls: Calls(1_000, 2_000)));
        Assert.Null(few.MedianModelMs);

        var snapshot = TurnQualityMetrics.Compute(Inputs(modelCalls:
            [.. Calls(1_000, 2_000, 3_000, 4_000, 5_000, 6_000, 7_000, 8_000, 9_000, 40_000), (90_000, UsageOutcomes.Failed)]));

        Assert.Equal(11, snapshot.ModelCalls);
        Assert.Equal(5_000, snapshot.MedianModelMs);
        Assert.Equal(9_000, snapshot.P90ModelMs);
        Assert.Equal(1 / 11d, snapshot.ModelFailureRate!.Value, 6);
        Assert.DoesNotContain("slow_replies", snapshot.Flags);
    }

    [Fact]
    public void Slow_replies_failing_turns_friction_and_a_cold_cache_are_flagged()
    {
        var snapshot = TurnQualityMetrics.Compute(new TurnQualityInputs(
            Calls(31_000, 32_000, 33_000, 34_000, 35_000),
            ModelInputTokens: 100_000,
            ModelCachedInputTokens: 5_000,
            Turns: [(1_000, "completed"), (2_000, "failed"), (3_000, "completed"), (4_000, "cancelled"), (5_000, "completed")],
            ToolCalls: [],
            ApprovalsAsked: 12,
            ApprovalsDenied: 3,
            AutoApproved: 2));

        Assert.Equal(["slow_replies", "failing_turns", "approval_friction", "low_cache"], snapshot.Flags);
        Assert.Equal(1, snapshot.FailedTurns);
        Assert.Equal(1, snapshot.CancelledTurns);
        Assert.Equal(0.05, snapshot.CacheHitRate!.Value, 6);
    }

    [Fact]
    public void Slow_tools_need_enough_calls_and_are_sorted_by_average_time()
    {
        TurnToolCall[] calls =
        [
            .. Enumerable.Range(0, 5).Select(_ => new TurnToolCall("BrowseTheWeb", ToolOutcomes.Completed, 9_000)),
            .. Enumerable.Range(0, 6).Select(index => new TurnToolCall("SearchMemory",
                index == 0 ? ToolOutcomes.Failed : ToolOutcomes.Completed, 200)),
            new TurnToolCall("RunCodingTask", ToolOutcomes.Completed, 120_000),
        ];

        var snapshot = TurnQualityMetrics.Compute(Inputs(toolCalls: calls));

        Assert.Equal(["BrowseTheWeb", "SearchMemory"], snapshot.SlowTools.Select(tool => tool.Tool));
        Assert.Equal(9_000, snapshot.SlowTools[0].AverageMs);
        Assert.Equal(1 / 12d, snapshot.ToolFailureRate!.Value, 6);
    }

    private static (int, string)[] Calls(params int[] durations) =>
        durations.Select(ms => (ms, UsageOutcomes.Completed)).ToArray();

    private static TurnQualityInputs Inputs(IReadOnlyList<(int, string)>? modelCalls = null,
        IReadOnlyList<TurnToolCall>? toolCalls = null) =>
        new(modelCalls ?? [], 0, 0, [], toolCalls ?? [], 0, 0, 0);
}
