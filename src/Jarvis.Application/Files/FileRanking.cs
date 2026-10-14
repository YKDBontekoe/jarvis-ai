using Jarvis.Application.Memory;

namespace Jarvis.Application.Files;

/// <summary>
/// Hybrid ranking for file chunks, the same fusion as <see cref="MemoryRanking"/> without the memory priors
/// (importance, recency, recall): keyword and semantic relevance are combined as a probabilistic OR, chunks below
/// <see cref="MinimumRelevance"/> are dropped, and a score-adaptive cut removes the weak tail.
/// </summary>
public static class FileRanking
{
    public const int MaxHits = 8;
    internal const double RelativeCutoff = 0.35;
    internal const double MinimumRelevance = 0.08;

    public static IReadOnlyList<FileSearchHit> Rank(IReadOnlyList<FileSearchHit> lexical,
        IReadOnlyList<FileSearchHit> semantic, int maxHits = MaxHits)
    {
        var chunks = new Dictionary<Guid, FileSearchHit>();
        var keyword = new Dictionary<Guid, double>();
        var meaning = new Dictionary<Guid, double>();
        foreach (var hit in lexical)
        {
            chunks[hit.ChunkId] = hit;
            keyword[hit.ChunkId] = Math.Max(keyword.GetValueOrDefault(hit.ChunkId), Math.Clamp(hit.Score, 0, 1));
        }
        // Embedding models spread similarities differently, so the absolute mapping is combined with the hit's
        // position between the list's typical and best similarity, as memory ranking does.
        var typical = semantic.Count == 0 ? 0 : semantic.Average(hit => hit.Score);
        var best = semantic.Count == 0 ? 0 : semantic.Max(hit => hit.Score);
        foreach (var hit in semantic)
        {
            chunks.TryAdd(hit.ChunkId, hit);
            var spread = best - typical;
            var relative = spread < 0.02 ? 1 : Math.Clamp((hit.Score - typical) / spread, 0, 1);
            meaning[hit.ChunkId] = MemoryRanking.SemanticRelevance(hit.Score) * (0.4 + 0.6 * relative);
        }

        var scored = chunks.Values
            .Select(hit => (Hit: hit, Relevance: 1 - (1 - keyword.GetValueOrDefault(hit.ChunkId)) *
                (1 - meaning.GetValueOrDefault(hit.ChunkId))))
            .Where(item => item.Relevance >= MinimumRelevance)
            .OrderByDescending(item => item.Relevance)
            .ThenBy(item => item.Hit.FileName, StringComparer.Ordinal)
            .ThenBy(item => item.Hit.ChunkIndex)
            .ToArray();
        if (scored.Length == 0) return [];
        var floor = scored[0].Relevance * RelativeCutoff;
        return scored.TakeWhile(item => item.Relevance >= floor)
            .Take(Math.Max(1, maxHits))
            .Select(item => item.Hit with { Score = Math.Round(item.Relevance, 4) })
            .ToArray();
    }
}
