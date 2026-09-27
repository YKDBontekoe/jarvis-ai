using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jarvis.Application.Learning;

namespace Jarvis.Agents.Learning;

/// <summary>
/// Process-local recall traces for dreaming. Hashes queries so retrieval quality can inform deep ranking
/// without persisting what the user searched for.
/// </summary>
public sealed class MemoryRecallTracker : IMemoryRecallTracker
{
    private readonly ConcurrentDictionary<(Guid Owner, Guid Memory), Bucket> _hits = new();

    public void Record(Guid ownerId, Guid memoryId, string query)
    {
        if (ownerId == Guid.Empty || memoryId == Guid.Empty || string.IsNullOrWhiteSpace(query)) return;
        var hash = Hash(query);
        var now = DateTimeOffset.UtcNow;
        _hits.AddOrUpdate((ownerId, memoryId),
            _ => new Bucket(1, [hash], now),
            (_, existing) =>
            {
                var queries = existing.Queries.Count >= 8 || existing.Queries.Contains(hash)
                    ? existing.Queries
                    : existing.Queries.Concat([hash]).ToArray();
                return new Bucket(existing.Hits + 1, queries, now);
            });
    }

    public IReadOnlyList<MemoryRecallRecord> Snapshot(Guid ownerId) =>
        _hits.Where(pair => pair.Key.Owner == ownerId)
            .Select(pair => new MemoryRecallRecord(pair.Key.Memory, pair.Value.Hits, pair.Value.Queries.Count,
                pair.Value.LastHitAt))
            .ToArray();

    internal static string Hash(string query)
    {
        var normalized = string.Join(' ', query.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private sealed record Bucket(int Hits, IReadOnlyList<string> Queries, DateTimeOffset LastHitAt);
}
