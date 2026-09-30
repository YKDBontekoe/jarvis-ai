using Jarvis.Application.Learning;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Memory;

namespace Jarvis.Agents.Learning;

/// <summary>
/// OpenClaw-style deep ranking: six weighted signals plus light/REM reinforcement, then threshold gates
/// that keep one-off chatter out of durable memory.
/// </summary>
internal static class DreamingRanker
{
    public const double RelevanceWeight = 0.30;
    public const double FrequencyWeight = 0.24;
    public const double DiversityWeight = 0.15;
    public const double RecencyWeight = 0.15;
    public const double ConsolidationWeight = 0.10;
    public const double RichnessWeight = 0.06;
    public const double MinScore = 0.80;
    public const int MinFrequency = 2;
    public const int MinUniqueSources = 2;
    public const double DuplicateJaccard = 0.90;
    public const double RelatedJaccard = 0.72;
    public const double MentionJaccard = 0.35;
    public const double MaxPhaseBoost = 0.08;

    public static double Score(DreamCandidate candidate, DateTimeOffset now)
    {
        var ageDays = Math.Max(0, (now - candidate.LastSeenAt).TotalDays);
        var recency = Math.Exp(-ageDays / 21d);
        var frequency = Math.Clamp(candidate.SignalCount / 3d, 0, 1);
        var diversity = Math.Clamp(candidate.UniqueSources / 3d, 0, 1);
        var consolidation = candidate.DaySpan <= 0 ? 0 : Math.Clamp(candidate.DaySpan / 7d, 0, 1);
        var boost = Math.Min(MaxPhaseBoost, candidate.LightBoost + candidate.RemBoost);
        return Math.Clamp(
            RelevanceWeight * candidate.Relevance
            + FrequencyWeight * frequency
            + DiversityWeight * diversity
            + RecencyWeight * recency
            + ConsolidationWeight * consolidation
            + RichnessWeight * candidate.Richness
            + boost, 0, 1);
    }

    public static bool PassesPromotionGate(DreamCandidate candidate, double score) =>
        score >= MinScore && candidate.SignalCount >= MinFrequency && candidate.UniqueSources >= MinUniqueSources;

    public static string Canonical(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static HashSet<string> Tokens(string value) =>
        value.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);

    public static double Jaccard(string left, string right) => Jaccard(left, right, null);

    /// <summary>Token Jaccard with an optional token cache; dreaming compares every memory with every other one.</summary>
    internal static double Jaccard(string left, string right, Dictionary<string, HashSet<string>>? cache)
    {
        var a = CachedTokens(left, cache);
        var b = CachedTokens(right, cache);
        if (a.Count == 0 || b.Count == 0) return Canonical(left) == Canonical(right) && Canonical(left).Length > 0 ? 1 : 0;
        var shared = a.Count(b.Contains);
        return shared / (double)(a.Count + b.Count - shared);
    }

    private static HashSet<string> CachedTokens(string value, Dictionary<string, HashSet<string>>? cache)
    {
        if (cache is null) return Tokens(value);
        if (!cache.TryGetValue(value, out var tokens)) cache[value] = tokens = Tokens(value);
        return tokens;
    }

    public static double Richness(string kind, string content)
    {
        var conceptual = kind is "fact" or "preference" or "relationship" or "decision" or "project" or "routine"
            ? 1d : 0.4;
        var tokens = Tokens(content);
        var density = Math.Clamp(tokens.Count / 12d, 0, 1);
        return Math.Clamp(0.7 * conceptual + 0.3 * density, 0, 1);
    }

    public static IReadOnlyList<DreamCandidate> Stage(
        IReadOnlyList<MemoryRecord> memories,
        IReadOnlyList<Message> messages,
        IReadOnlyList<MemoryRecallRecord> recalls,
        DateTimeOffset now)
    {
        var recallById = recalls
            .GroupBy(item => item.MemoryId)
            .ToDictionary(group => group.Key, group => (
                Hits: group.Sum(item => item.Hits),
                Queries: group.Max(item => item.UniqueQueries),
                Last: group.Max(item => item.LastHitAt)));

        var tokens = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var userMessages = messages.Where(message => message.Role == "user").ToArray();
        var staged = new List<DreamCandidate>();
        foreach (var memory in memories.Where(item => item.ValidUntil is null || item.ValidUntil > now))
        {
            recallById.TryGetValue(memory.Id, out var recall);
            var mentioned = userMessages
                .Where(message => Jaccard(memory.Content, message.Content, tokens) >= MentionJaccard)
                .ToArray();
            var mentions = mentioned.Length;
            var mentionDays = mentioned
                .Select(message => DateOnly.FromDateTime(message.CreatedAt.UtcDateTime))
                .Distinct()
                .Count();
            // Recall counts persist on the memory itself, so nightly dreaming in the worker sees what chat recalled in
            // the API process. The in-process tracker overlaps with them, so take the larger rather than the sum.
            var recallHits = Math.Max(recall.Hits, memory.AccessCount);
            // A memory recalled on a later day than it was written is a spaced-repetition signal: it stayed useful.
            var recalledLater = memory.LastAccessedAt is { } accessed && accessed.Date > memory.CreatedAt.Date ? 1 : 0;
            var signalCount = Math.Max(1, 1 + mentions + recallHits);
            var uniqueSources = 1 + mentionDays + Math.Max(Math.Max(0, recall.Queries), recalledLater);
            var first = memory.CreatedAt;
            var last = new[]
            {
                memory.UpdatedAt, recall.Last == default ? memory.UpdatedAt : recall.Last,
                memory.LastAccessedAt ?? memory.UpdatedAt
            }.Max();
            var daySpan = Math.Max(0, (last.Date - first.Date).Days);
            staged.Add(new DreamCandidate(
                Canonical(memory.Content),
                memory.Kind,
                memory.Content,
                memory.Id,
                memory.SourceId,
                memory.IsPinned,
                memory.Importance,
                memory.Confidence,
                first,
                last,
                signalCount,
                uniqueSources,
                daySpan,
                Math.Clamp(memory.Importance * memory.Confidence, 0, 1),
                Richness(memory.Kind, memory.Content),
                mentions > 0 || recallHits > 0 ? 0.04 : 0,
                0));
        }

        MergeNearDuplicates(staged, tokens);

        var leftover = userMessages
            .Where(item => item.Content.Length >= 12)
            .Where(item => staged.All(candidate => Jaccard(candidate.Content, item.Content, tokens) < MentionJaccard))
            .ToList();
        while (leftover.Count > 0)
        {
            var message = leftover[0];
            var cluster = leftover.Where(other => Jaccard(message.Content, other.Content, tokens) >= RelatedJaccard).ToArray();
            leftover.RemoveAll(cluster.Contains);
            if (cluster.Length < 2) continue;
            var first = cluster.Min(item => item.CreatedAt);
            var last = cluster.Max(item => item.CreatedAt);
            staged.Add(new DreamCandidate(
                Canonical(message.Content),
                "fact",
                message.Content.Length <= 500 ? message.Content : message.Content[..500],
                null,
                message.Id,
                false,
                0.7f,
                0.85f,
                first,
                last,
                cluster.Length,
                cluster.Select(item => DateOnly.FromDateTime(item.CreatedAt.UtcDateTime)).Distinct().Count(),
                Math.Max(0, (last.Date - first.Date).Days),
                0.7,
                Richness("fact", message.Content),
                0.04,
                0));
        }

        return staged;
    }

    /// <summary>Collapse near-duplicate memory candidates so frequency accumulates on one keeper.</summary>
    internal static void MergeNearDuplicates(List<DreamCandidate> staged,
        Dictionary<string, HashSet<string>>? tokens = null)
    {
        tokens ??= new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var index = 0; index < staged.Count; index++)
        {
            var keeper = staged[index];
            if (keeper.MemoryId is null) continue;
            for (var other = staged.Count - 1; other > index; other--)
            {
                var candidate = staged[other];
                if (candidate.MemoryId is null || keeper.Kind != candidate.Kind) continue;
                if (Jaccard(keeper.Content, candidate.Content, tokens) < RelatedJaccard) continue;
                staged[index] = Combine(keeper, candidate);
                keeper = staged[index];
                staged.RemoveAt(other);
            }
        }
    }

    public static DreamCandidate Combine(DreamCandidate left, DreamCandidate right)
    {
        var keeper = Prefer(left, right);
        var other = ReferenceEquals(keeper, left) ? right : left;
        var first = left.FirstSeenAt < right.FirstSeenAt ? left.FirstSeenAt : right.FirstSeenAt;
        var last = left.LastSeenAt > right.LastSeenAt ? left.LastSeenAt : right.LastSeenAt;
        return keeper with
        {
            SignalCount = left.SignalCount + other.SignalCount,
            UniqueSources = left.UniqueSources + other.UniqueSources,
            FirstSeenAt = first,
            LastSeenAt = last,
            DaySpan = Math.Max(0, (last.Date - first.Date).Days),
            LightBoost = Math.Min(MaxPhaseBoost, left.LightBoost + right.LightBoost + 0.02),
            Relevance = Math.Max(left.Relevance, right.Relevance),
            Importance = Math.Max(left.Importance, right.Importance),
            Confidence = Math.Max(left.Confidence, right.Confidence),
            Duplicates = keeper.DuplicateIds.Concat(other.MemoryId is { } id ? [id] : []).Concat(other.DuplicateIds)
                .Distinct().ToArray()
        };
    }

    public static DreamCandidate Prefer(DreamCandidate left, DreamCandidate right)
    {
        if (left.IsPinned != right.IsPinned) return left.IsPinned ? left : right;
        var leftRank = left.Confidence * left.Importance;
        var rightRank = right.Confidence * right.Importance;
        if (Math.Abs(leftRank - rightRank) > 0.02) return leftRank >= rightRank ? left : right;
        return left.LastSeenAt >= right.LastSeenAt ? left : right;
    }
}

internal sealed record DreamCandidate(
    string Key,
    string Kind,
    string Content,
    Guid? MemoryId,
    Guid? SourceMessageId,
    bool IsPinned,
    float Importance,
    float Confidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int SignalCount,
    int UniqueSources,
    int DaySpan,
    double Relevance,
    double Richness,
    double LightBoost,
    double RemBoost,
    IReadOnlyList<Guid>? Duplicates = null)
{
    public IReadOnlyList<Guid> DuplicateIds => Duplicates ?? [];
}
