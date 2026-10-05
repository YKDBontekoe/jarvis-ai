namespace Jarvis.Application.Decisions;

/// <summary>One resolved prediction: the chance the owner gave and whether it came true.</summary>
public readonly record struct ResolvedPrediction(double Probability, bool Outcome, DateTimeOffset ResolvedAt);

/// <summary>Predictions grouped by stated confidence, so "I said 70%" can be compared with how often it happened.</summary>
public sealed record CalibrationBucket(string Label, int Count, double? MeanPredicted, double? ActualRate);

/// <summary>
/// How well the owner's confidence matches reality. <see cref="Brier"/> runs from 0 (perfect) to 1; always saying
/// 50% scores 0.25. <see cref="Trend"/> compares the latest 10 resolved calls with the 10 before.
/// </summary>
public sealed record CalibrationReport(
    int Resolved,
    double? Brier,
    double? MeanPredicted,
    double? HitRate,
    IReadOnlyList<CalibrationBucket> Buckets,
    double? RecentBrier,
    double? PreviousBrier,
    string? Trend);

/// <summary>Pure calibration maths, so it is easy to test and the weekly review can reuse it.</summary>
public static class CalibrationCalculator
{
    public const int TrendWindow = 10;
    public const int MinTrendSample = 5;
    public const double TrendTolerance = 0.02;

    private static readonly (string Label, double Upper)[] BucketEdges =
    [
        ("Under 30%", 0.30), ("30–50%", 0.50), ("50–70%", 0.70), ("70–90%", 0.90), ("90%+", double.MaxValue)
    ];

    /// <summary>The mean squared gap between stated chance and outcome, or null with nothing to score.</summary>
    public static double? Brier(IEnumerable<ResolvedPrediction> predictions)
    {
        var list = predictions as IReadOnlyCollection<ResolvedPrediction> ?? predictions.ToArray();
        if (list.Count == 0) return null;
        return Math.Round(list.Average(p => Math.Pow(p.Probability - (p.Outcome ? 1.0 : 0.0), 2)), 3);
    }

    public static CalibrationReport Report(IReadOnlyList<ResolvedPrediction> predictions)
    {
        if (predictions.Count == 0)
            return new CalibrationReport(0, null, null, null, Buckets(predictions), null, null, null);

        var ordered = predictions.OrderByDescending(p => p.ResolvedAt).ToArray();
        var recent = ordered.Take(TrendWindow).ToArray();
        var previous = ordered.Skip(TrendWindow).Take(TrendWindow).ToArray();
        double? recentBrier = Brier(recent);
        double? previousBrier = Brier(previous);
        string? trend = null;
        if (recent.Length >= MinTrendSample && previous.Length >= MinTrendSample)
        {
            var improvement = previousBrier!.Value - recentBrier!.Value;
            trend = improvement > TrendTolerance ? "improving"
                : improvement < -TrendTolerance ? "worsening" : "steady";
        }

        return new CalibrationReport(predictions.Count, Brier(predictions),
            Math.Round(predictions.Average(p => p.Probability), 3),
            Math.Round(predictions.Count(p => p.Outcome) / (double)predictions.Count, 3),
            Buckets(predictions), recentBrier, previousBrier, trend);
    }

    private static IReadOnlyList<CalibrationBucket> Buckets(IReadOnlyList<ResolvedPrediction> predictions)
    {
        var buckets = new List<CalibrationBucket>(BucketEdges.Length);
        var lower = 0.0;
        foreach (var (label, upper) in BucketEdges)
        {
            var inside = predictions.Where(p => p.Probability >= lower && p.Probability < upper).ToArray();
            buckets.Add(new CalibrationBucket(label, inside.Length,
                inside.Length == 0 ? null : Math.Round(inside.Average(p => p.Probability), 3),
                inside.Length == 0 ? null : Math.Round(inside.Count(p => p.Outcome) / (double)inside.Length, 3)));
            lower = upper;
        }

        return buckets;
    }
}
