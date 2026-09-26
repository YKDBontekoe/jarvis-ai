using Jarvis.Application.Files;
using Jarvis.Application.Memory;

namespace Jarvis.Infrastructure.Files;

public sealed class FileSearchService(IFileContentRepository repository) : IFileSearchService
{
    public async Task<IReadOnlyList<FileSearchHit>> SearchAsync(Guid ownerId, string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 2_000) return [];

        var textResults = await repository.SearchTextAsync(ownerId, query, cancellationToken);
        var all = textResults.DistinctBy(hit => (hit.FileId, hit.ChunkIndex))
            .ToDictionary(hit => (hit.FileId, hit.ChunkIndex));
        var scores = new Dictionary<(Guid FileId, int ChunkIndex), double>();
        AddRanks(textResults, scores);
        return scores.Select(item => all[item.Key] with { Score = item.Value })
            .OrderByDescending(item => item.Score).Take(8).ToArray();
    }

    private static void AddRanks(IReadOnlyList<FileSearchHit> results, IDictionary<(Guid, int), double> scores)
    {
        for (var index = 0; index < results.Count; index++)
        {
            var key = (results[index].FileId, results[index].ChunkIndex);
            scores[key] = (scores.TryGetValue(key, out var current) ? current : 0) + 1d / (60 + index + 1);
        }
    }
}
