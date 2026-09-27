using System.Diagnostics;
using System.Diagnostics.Metrics;
using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;

namespace Jarvis.Memory;

public sealed class MemoryService(IMemoryRepository repository) : IMemoryService
{
    public async Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance,
        float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken,
        string sourceType = "user", Guid? sourceId = null)
    {
        Validate(kind, content, importance, confidence);
        if (sourceType is not ("user" or "conversation") || sourceId == Guid.Empty)
            throw new ArgumentException("Memory source metadata is invalid.", nameof(sourceType));
        return await repository.CreateAsync(ownerId, kind, content.Trim(), importance, confidence,
            sourceType, sourceId, validUntil, isPinned, cancellationToken);
    }

    public async Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
        float importance, float confidence, CancellationToken cancellationToken,
        string sourceType = "conversation", Guid? sourceId = null)
    {
        Validate(kind, content, importance, confidence);
        if (sourceType is not ("user" or "conversation") || sourceId == Guid.Empty)
            throw new ArgumentException("Memory source metadata is invalid.", nameof(sourceType));
        return await repository.ReplaceAsync(existingId, ownerId, kind, content.Trim(), importance, confidence,
            sourceType, sourceId, cancellationToken);
    }

    public Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetAsync(id, ownerId, cancellationToken);

    public Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken)
    {
        if (!MemoryKinds.IsValidFilter(kind)) throw new ArgumentException("Unknown memory kind.", nameof(kind));
        return repository.ListAsync(ownerId, kind, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        repository.ListPinnedAsync(ownerId, cancellationToken);

    public async Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
        float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken)
    {
        Validate(kind, content, importance, confidence);
        return await repository.UpdateAsync(id, ownerId, kind, content.Trim(), importance, confidence,
            MemoryValidity.ForUpdate(validUntil), isPinned, cancellationToken);
    }

    public Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
        CancellationToken cancellationToken, string? kind = null)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "completed";
        IReadOnlyList<MemorySearchHit> hits = [];
        using var activity = MemoryDiagnostics.ActivitySource.StartActivity("jarvis.memory.search");
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return hits;
            if (!MemoryKinds.IsValidFilter(kind))
                throw new ArgumentException("Unknown memory kind.", nameof(kind));
            if (!await repository.HasActiveMemoriesAsync(ownerId, cancellationToken)) return hits;

            var textResults = await repository.SearchTextAsync(ownerId, query, kind, cancellationToken);
            var trigramResults = await repository.SearchTrigramAsync(ownerId, query, kind, cancellationToken);
            var scores = new Dictionary<Guid, double>();
            AddRanks(textResults, scores);
            AddRanks(trigramResults, scores);

            var all = textResults.Concat(trigramResults).DistinctBy(x => x.Id).ToDictionary(x => x.Id);
            hits = scores.Select(pair => new MemorySearchHit(all[pair.Key], pair.Value + RecencyBoost(all[pair.Key])))
                .OrderByDescending(x => x.Score).Take(8).ToList();
            activity?.SetTag("jarvis.memory.hit_count", hits.Count);
            return hits;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception exception)
        {
            outcome = "failed";
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            var tags = new TagList { { "search.outcome", outcome } };
            MemoryDiagnostics.SearchDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
            MemoryDiagnostics.SearchResults.Add(hits.Count, tags);
        }
    }

    private static void AddRanks(IReadOnlyList<MemoryRecord> memories, Dictionary<Guid, double> scores)
    {
        for (var index = 0; index < memories.Count; index++)
        {
            var memory = memories[index];
            scores[memory.Id] = scores.GetValueOrDefault(memory.Id) + (1d / (60 + index + 1));
        }
    }

    private static double RecencyBoost(MemoryRecord memory)
    {
        var ageDays = Math.Max(0, (DateTimeOffset.UtcNow - memory.UpdatedAt).TotalDays);
        return (memory.Importance * 0.002) + (0.001 / (1 + ageDays / 30));
    }

    private static void Validate(string kind, string content, float importance, float confidence)
    {
        if (!MemoryKinds.IsValid(kind)) throw new ArgumentException("Unknown memory kind.", nameof(kind));
        if (string.IsNullOrWhiteSpace(content) || content.Length > 8_000) throw new ArgumentException("Memory content must contain 1 to 8,000 characters.", nameof(content));
        if (importance is < 0 or > 1 || confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(importance), "Importance and confidence must be between zero and one.");
    }
}
