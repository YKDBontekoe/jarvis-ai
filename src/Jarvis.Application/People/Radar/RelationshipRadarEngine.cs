using System.Globalization;

namespace Jarvis.Application.People.Radar;

/// <summary>
/// Looks at when messages were sent, never what they said, and compares the last 30 days with the 90 days before.
/// It notices a conversation going quiet, the owner replying slower, one side doing most of the reaching out, and a
/// message left unanswered. Pure and deterministic, so it is easy to test; every signal needs enough history that
/// a quiet chat does not raise a false alarm.
/// </summary>
public static class RelationshipRadarEngine
{
    public const int MinBaselineMessages = 12;
    public const double QuietRatio = 0.4;
    public const double VeryQuietRatio = 0.15;
    public const int MinReplies = 5;
    public const double SlowerFactor = 2.0;
    public const double SlowerMinExtraMinutes = 60;
    public const int MinConversationStarts = 4;
    public const double LopsidedShare = 0.85;
    public const double BalancedShareCeiling = 0.65;
    public const double RareShare = 0.15;
    public const double BalancedShareFloor = 0.35;
    public static readonly TimeSpan NewConversationGap = TimeSpan.FromHours(6);
    public static readonly TimeSpan MaxReplyLatency = TimeSpan.FromDays(14);
    public static readonly TimeSpan UnansweredAfter = TimeSpan.FromHours(48);
    public static readonly TimeSpan UnansweredUrgentAfter = TimeSpan.FromDays(5);

    /// <summary>Older than this, an unanswered message is a conversation that ended, not one to chase.</summary>
    public static readonly TimeSpan UnansweredMaxAge = TimeSpan.FromDays(45);
    public const int SparklineWeeks = 8;

    public static RadarReport Analyze(Guid personId, string name, IReadOnlyList<RadarMessage> messages,
        DateTimeOffset now, int? toneScore = null, string? toneReason = null)
    {
        var sorted = messages.Where(m => m.SentAt <= now).OrderBy(m => m.SentAt).ToArray();
        var recentStart = now.AddDays(-RadarRules.RecentDays);
        var baselineStart = now.AddDays(-RadarRules.HistoryDays);
        var recent = sorted.Where(m => m.SentAt > recentStart).ToArray();
        var baseline = sorted.Where(m => m.SentAt > baselineStart && m.SentAt <= recentStart).ToArray();
        var recentRate = Math.Round(recent.Length / (RadarRules.RecentDays / 7.0), 2);
        var baselineRate = Math.Round(baseline.Length / (RadarRules.BaselineDays / 7.0), 2);

        var latencies = ReplyLatencies(sorted);
        var recentLatency = Median(latencies.Where(l => l.ReplyAt > recentStart).Select(l => l.Minutes).ToArray());
        var baselineLatency = Median(latencies
            .Where(l => l.ReplyAt > baselineStart && l.ReplyAt <= recentStart).Select(l => l.Minutes).ToArray());
        var recentReplies = latencies.Count(l => l.ReplyAt > recentStart);
        var baselineReplies = latencies.Count(l => l.ReplyAt > baselineStart && l.ReplyAt <= recentStart);

        var starts = ConversationStarts(sorted);
        var recentStarts = starts.Where(s => s.At > recentStart).ToArray();
        var baselineStarts = starts.Where(s => s.At > baselineStart && s.At <= recentStart).ToArray();
        double? recentShare = recentStarts.Length >= MinConversationStarts
            ? Math.Round(recentStarts.Count(s => s.FromMe) / (double)recentStarts.Length, 2) : null;
        double? baselineShare = baselineStarts.Length >= MinConversationStarts
            ? Math.Round(baselineStarts.Count(s => s.FromMe) / (double)baselineStarts.Length, 2) : null;

        var (unanswered, unansweredHours) = Unanswered(sorted, now);
        var signals = new List<RadarSignal>();

        if (baseline.Length >= MinBaselineMessages && recentRate <= baselineRate * QuietRatio)
        {
            signals.Add(new RadarSignal(RadarSignalKinds.Quiet,
                recentRate <= baselineRate * VeryQuietRatio ? 2 : 1,
                recent.Length == 0
                    ? $"No messages in {RadarRules.RecentDays} days"
                    : "The conversation has gone quiet",
                $"{PerWeek(recentRate)} lately, against {PerWeek(baselineRate)} before."));
        }

        if (recentReplies >= MinReplies && baselineReplies >= MinReplies &&
            recentLatency is { } slowNow && baselineLatency is { } slowBefore &&
            slowNow >= slowBefore * SlowerFactor && slowNow - slowBefore >= SlowerMinExtraMinutes)
        {
            signals.Add(new RadarSignal(RadarSignalKinds.ReplySlower, 1, "You reply slower than you used to",
                $"Usually {Duration(slowBefore)}, lately {Duration(slowNow)}."));
        }

        if (recentShare is { } mine && baselineShare is { } before)
        {
            if (mine >= LopsidedShare && before <= BalancedShareCeiling)
                signals.Add(new RadarSignal(RadarSignalKinds.YouInitiate, 1,
                    "You are doing most of the reaching out",
                    $"You started {Percent(mine)} of conversations lately, against {Percent(before)} before."));
            else if (mine <= RareShare && before >= BalancedShareFloor)
                signals.Add(new RadarSignal(RadarSignalKinds.TheyInitiate, 1,
                    "They are reaching out more than you",
                    $"You started {Percent(mine)} of conversations lately, against {Percent(before)} before."));
        }

        if (unansweredHours is { } hours && TimeSpan.FromHours(hours) >= UnansweredAfter &&
            TimeSpan.FromHours(hours) <= UnansweredMaxAge)
        {
            signals.Add(new RadarSignal(RadarSignalKinds.Unanswered,
                TimeSpan.FromHours(hours) >= UnansweredUrgentAfter ? 2 : 1,
                unanswered == 1
                    ? $"They wrote {Ago(hours)} and you have not replied"
                    : $"They sent {unanswered} messages, the first {Ago(hours)}, and you have not replied",
                "The last message in the chat is theirs."));
        }

        if (toneScore is <= -1)
            signals.Add(new RadarSignal(RadarSignalKinds.Tone, 1, "Recent messages sound a little cool",
                (string.IsNullOrWhiteSpace(toneReason) ? "" : toneReason + " ") + "This is a guess."));

        return new RadarReport(personId, name, sorted.Length == 0 ? null : sorted[^1].SentAt,
            recent.Count(m => m.FromMe), recent.Count(m => !m.FromMe), recentRate, baselineRate,
            recentLatency, baselineLatency, recentShare, baselineShare, unanswered, unansweredHours,
            Weekly(sorted, now), signals.OrderByDescending(s => s.Severity).ToArray(), toneScore, toneReason);
    }

    /// <summary>
    /// For each run of messages from them that opened a conversation (six hours or more after anything before it)
    /// and that the owner then answers, how long the owner took, counted from the first message of the run. A
    /// message from them that merely answers the owner is not something to reply to, so when the owner always
    /// starts the conversation their next opener is not counted as a slow reply. Answers after more than two weeks
    /// are a new conversation too.
    /// </summary>
    internal static IReadOnlyList<(DateTimeOffset ReplyAt, double Minutes)> ReplyLatencies(
        IReadOnlyList<RadarMessage> sorted)
    {
        var result = new List<(DateTimeOffset, double)>();
        DateTimeOffset? runStart = null;
        RadarMessage? previous = null;
        foreach (var message in sorted)
        {
            if (!message.FromMe)
            {
                if (runStart is null && (previous is null || message.SentAt - previous.Value.SentAt >= NewConversationGap))
                    runStart = message.SentAt;
            }
            else if (runStart is { } started)
            {
                var wait = message.SentAt - started;
                if (wait <= MaxReplyLatency) result.Add((message.SentAt, wait.TotalMinutes));
                runStart = null;
            }

            previous = message;
        }

        return result;
    }

    /// <summary>A message that comes six hours or more after the previous one starts a conversation.</summary>
    internal static IReadOnlyList<(DateTimeOffset At, bool FromMe)> ConversationStarts(
        IReadOnlyList<RadarMessage> sorted)
    {
        var result = new List<(DateTimeOffset, bool)>();
        RadarMessage? previous = null;
        foreach (var message in sorted)
        {
            if (previous is null || message.SentAt - previous.Value.SentAt >= NewConversationGap)
                result.Add((message.SentAt, message.FromMe));
            previous = message;
        }

        return result;
    }

    /// <summary>The unbroken run of messages from them at the end of the chat, and how long ago it began.</summary>
    internal static (int Count, double? Hours) Unanswered(IReadOnlyList<RadarMessage> sorted, DateTimeOffset now)
    {
        var count = 0;
        for (var i = sorted.Count - 1; i >= 0 && !sorted[i].FromMe; i--) count++;
        if (count == 0) return (0, null);
        var first = sorted[sorted.Count - count];
        return (count, Math.Round((now - first.SentAt).TotalHours, 1));
    }

    private static IReadOnlyList<int> Weekly(IReadOnlyList<RadarMessage> sorted, DateTimeOffset now)
    {
        var buckets = new int[SparklineWeeks];
        foreach (var message in sorted)
        {
            var weeksAgo = (int)Math.Floor((now - message.SentAt).TotalDays / 7.0);
            if (weeksAgo is >= 0 and < SparklineWeeks) buckets[SparklineWeeks - 1 - weeksAgo]++;
        }

        return buckets;
    }

    internal static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        var ordered = values.OrderBy(v => v).ToArray();
        var middle = ordered.Length / 2;
        return Math.Round(ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2, 1);
    }

    private static string PerWeek(double rate) =>
        rate.ToString("0.#", CultureInfo.InvariantCulture) + (rate == 1 ? " message a week" : " messages a week");

    private static string Percent(double share) =>
        (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    internal static string Duration(double minutes) => minutes switch
    {
        < 90 => $"{Math.Max(1, (int)Math.Round(minutes))} min",
        < 36 * 60 => $"{(int)Math.Round(minutes / 60)} h",
        _ => $"{(int)Math.Round(minutes / (24 * 60))} days"
    };

    internal static string Ago(double hours) => hours switch
    {
        < 1 => "just now",
        < 48 => $"{(int)Math.Round(hours)} hours ago",
        _ => $"{(int)Math.Round(hours / 24)} days ago"
    };
}
