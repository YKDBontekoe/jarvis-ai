using Jarvis.Domain.Memory;

namespace Jarvis.Application.Memory;

/// <summary>
/// Hybrid ranking for memory recall. Relevance comes first: keyword and semantic scores are fused as a
/// probabilistic OR, so agreement between the two raises a memory without penalising one that only a single
/// retriever found. Importance, recency (refreshed by recall), and how often a memory proved useful then scale
/// relevance, in the spirit of the recency/importance/relevance retrieval score from Generative Agents. Finally a
/// score-adaptive cut drops the weak tail instead of always returning a fixed number of memories, and near
/// duplicates are skipped so the context holds distinct facts.
/// </summary>
public static class MemoryRanking
{
    public const int MaxHits = 8;
    internal const double RelativeCutoff = 0.35;
    internal const double MinimumRelevance = 0.08;
    internal const double RecencyHalfLifeDays = 90;
    internal const double DuplicateOverlap = 0.85;

    public static IReadOnlyList<MemorySearchHit> Rank(IReadOnlyList<MemoryLexicalMatch> lexical,
        IReadOnlyList<MemorySearchHit> semantic, DateTimeOffset now, int maxHits = MaxHits)
    {
        var records = new Dictionary<Guid, MemoryRecord>();
        var keyword = new Dictionary<Guid, double>();
        var meaning = new Dictionary<Guid, double>();
        foreach (var match in lexical)
        {
            records[match.Memory.Id] = match.Memory;
            keyword[match.Memory.Id] = Math.Clamp(match.TextScore + 0.5 * match.FuzzyScore, 0, 1);
        }
        // Embedding models differ a lot in how similarities are spread (some put unrelated text at 0.8, others at 0.1),
        // so the absolute mapping is combined with the hit's position between the list's typical and best similarity.
        var typical = semantic.Count == 0 ? 0 : semantic.Average(hit => hit.Score);
        var best = semantic.Count == 0 ? 0 : semantic.Max(hit => hit.Score);
        foreach (var hit in semantic)
        {
            records.TryAdd(hit.Memory.Id, hit.Memory);
            var spread = best - typical;
            var relative = spread < 0.02 ? 1 : Math.Clamp((hit.Score - typical) / spread, 0, 1);
            meaning[hit.Memory.Id] = SemanticRelevance(hit.Score) * (0.4 + 0.6 * relative);
        }

        var scored = records.Values
            .Select(memory =>
            {
                var relevance = 1 - (1 - keyword.GetValueOrDefault(memory.Id)) * (1 - meaning.GetValueOrDefault(memory.Id));
                return (Memory: memory, Relevance: relevance, Score: relevance * (1 + Prior(memory, now)));
            })
            .Where(item => item.Relevance >= MinimumRelevance)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Memory.UpdatedAt)
            .ToArray();
        if (scored.Length == 0) return [];

        var floor = scored[0].Score * RelativeCutoff;
        var selected = new List<MemorySearchHit>();
        var selectedTokens = new List<HashSet<string>>();
        foreach (var item in scored)
        {
            if (selected.Count >= maxHits || item.Score < floor) break;
            var tokens = MemoryQuery.Tokenize(item.Memory.Content).ToHashSet(StringComparer.Ordinal);
            if (selectedTokens.Any(other => Overlap(tokens, other) >= DuplicateOverlap)) continue;
            selected.Add(new MemorySearchHit(item.Memory, Math.Round(item.Score, 4)));
            selectedTokens.Add(tokens);
        }
        return selected;
    }

    /// <summary>
    /// Merges the hits for a short message searched on its own with the hits for the same message searched together with
    /// the previous message. Context hits count at <paramref name="contextWeight"/> so that a topic switch (where the
    /// previous message is unrelated) cannot push out what the message itself finds.
    /// </summary>
    public static IReadOnlyList<MemorySearchHit> MergeWithContext(IReadOnlyList<MemorySearchHit> alone,
        IReadOnlyList<MemorySearchHit> withContext, double contextWeight, int maxHits = MaxHits)
    {
        var best = new Dictionary<Guid, MemorySearchHit>();
        foreach (var hit in alone) best[hit.Memory.Id] = hit;
        foreach (var hit in withContext)
        {
            var weighted = hit with { Score = hit.Score * contextWeight };
            if (!best.TryGetValue(hit.Memory.Id, out var seen) || weighted.Score > seen.Score) best[hit.Memory.Id] = weighted;
        }
        return best.Values.OrderByDescending(hit => hit.Score).Take(maxHits).ToArray();
    }

    /// <summary>Maps cosine similarity (0.3 is the search floor, 0.8 is a near paraphrase) onto 0-1.</summary>
    public static double SemanticRelevance(double similarity) => Math.Clamp((similarity - 0.3) / 0.5, 0, 1);

    /// <summary>
    /// Up to +45% on relevance: importance, a recency curve that recall resets, and a log-scaled use count.
    /// Pinned memories are already always in context, so they get no extra boost here.
    /// </summary>
    public static double Prior(MemoryRecord memory, DateTimeOffset now) =>
        0.2 * Math.Clamp(memory.Importance, 0, 1) + 0.15 * Recency(memory, now) + 0.1 * Usage(memory.AccessCount);

    public static double Recency(MemoryRecord memory, DateTimeOffset now)
    {
        var touched = memory.LastAccessedAt is { } accessed && accessed > memory.UpdatedAt ? accessed : memory.UpdatedAt;
        var ageDays = Math.Max(0, (now - touched).TotalDays);
        return Math.Pow(0.5, ageDays / RecencyHalfLifeDays);
    }

    public static double Usage(int accessCount) => Math.Clamp(Math.Log(1 + Math.Max(0, accessCount)) / Math.Log(21), 0, 1);

    private static double Overlap(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var shared = left.Count(right.Contains);
        return shared / (double)Math.Min(left.Count, right.Count);
    }
}
