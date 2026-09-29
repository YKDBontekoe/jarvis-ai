using Jarvis.Application.Files;
using Jarvis.Application.Memory;

namespace Jarvis.Infrastructure.Files;

public sealed class FileSearchService(IFileContentRepository repository) : IFileSearchService
{
    public async Task<IReadOnlyList<FileSearchHit>> SearchAsync(Guid ownerId, string query, CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? fileIds = null)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 2_000) return [];
        if (fileIds is { Count: 0 }) return [];

        var textResults = await repository.SearchTextAsync(ownerId, query, cancellationToken, fileIds);
        var all = textResults.DistinctBy(hit => hit.ChunkId)
            .ToDictionary(hit => hit.ChunkId);
        var scores = new Dictionary<Guid, double>();
        AddRanks(textResults, scores);
        return scores.Select(item => all[item.Key] with { Score = item.Value })
            .OrderByDescending(item => item.Score).Take(8).ToArray();
    }

    private static void AddRanks(IReadOnlyList<FileSearchHit> results, IDictionary<Guid, double> scores)
    {
        for (var index = 0; index < results.Count; index++)
        {
            var key = results[index].ChunkId;
            scores[key] = (scores.TryGetValue(key, out var current) ? current : 0) + 1d / (60 + index + 1);
        }
    }
}
