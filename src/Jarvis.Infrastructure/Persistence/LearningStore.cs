using System.Text.Json;
using Jarvis.Application.Learning;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class LearningSignalEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? ProfileId { get; set; }
    public string? Tool { get; set; }
    public string? Category { get; set; }
    public string? ErrorKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public LearningSignalRecord ToRecord() => new(Id, OwnerId, Kind, ConversationId, MessageId, ProfileId, Tool,
        Category, ErrorKind, CreatedAt);
}

public sealed class TurnTraceEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? ProfileId { get; set; }
    public string Kind { get; set; } = TurnKinds.Interactive;
    public string MemoryIdsJson { get; set; } = "[]";
    public string SkillsJson { get; set; } = "[]";
    public string ToolsJson { get; set; } = "[]";
    public int TotalMs { get; set; }
    public string Outcome { get; set; } = "completed";
    public DateTimeOffset CreatedAt { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TurnTraceRecord ToRecord() => new(Id, OwnerId, ConversationId, MessageId, ProfileId, Kind,
        Read<Guid>(MemoryIdsJson), Read<string>(SkillsJson), Read<TurnToolCall>(ToolsJson), TotalMs, Outcome, CreatedAt);

    public static string Write<T>(IReadOnlyList<T> items) => JsonSerializer.Serialize(items, Json);

    private static IReadOnlyList<T> Read<T>(string json)
    {
        try { return JsonSerializer.Deserialize<List<T>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }
}

public sealed class LearningStore(JarvisDbContext db) : ILearningStore
{
    private const int MaxToolsPerTrace = 40;

    public async Task AddSignalAsync(LearningSignalRecord signal, CancellationToken cancellationToken)
    {
        db.LearningSignals.Add(new LearningSignalEntity
        {
            Id = signal.Id, OwnerId = signal.OwnerId, Kind = signal.Kind, ConversationId = signal.ConversationId,
            MessageId = signal.MessageId, ProfileId = signal.ProfileId, Tool = Limit(signal.Tool, 120),
            Category = Limit(signal.Category, 120), ErrorKind = Limit(signal.ErrorKind, 60),
            CreatedAt = signal.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddTraceAsync(TurnTraceRecord trace, CancellationToken cancellationToken)
    {
        db.TurnTraces.Add(new TurnTraceEntity
        {
            Id = trace.Id, OwnerId = trace.OwnerId, ConversationId = trace.ConversationId,
            MessageId = trace.MessageId, ProfileId = trace.ProfileId, Kind = trace.Kind,
            MemoryIdsJson = TurnTraceEntity.Write(trace.MemoryIds),
            SkillsJson = TurnTraceEntity.Write(trace.Skills.Select(name => Limit(name, 64)!).ToArray()),
            ToolsJson = TurnTraceEntity.Write(trace.Tools.Take(MaxToolsPerTrace)
                .Select(call => call with { Tool = Limit(call.Tool, 120)! }).ToArray()),
            TotalMs = Math.Max(0, trace.TotalMs), Outcome = trace.Outcome, CreatedAt = trace.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LearningSignalRecord>> ListSignalsAsync(Guid ownerId, DateTimeOffset since,
        CancellationToken cancellationToken) =>
        (await db.LearningSignals.AsNoTracking()
            .Where(signal => signal.OwnerId == ownerId && signal.CreatedAt >= since)
            .OrderBy(signal => signal.CreatedAt)
            .ToListAsync(cancellationToken)).Select(signal => signal.ToRecord()).ToArray();

    public async Task<IReadOnlyList<TurnTraceRecord>> ListTracesAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken) =>
        (await db.TurnTraces.AsNoTracking()
            .Where(trace => trace.OwnerId == ownerId && trace.CreatedAt >= since)
            .OrderByDescending(trace => trace.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)).Select(trace => trace.ToRecord()).ToArray();

    public async Task<int> PruneAsync(Guid ownerId, DateTimeOffset tracesBefore, DateTimeOffset signalsBefore,
        CancellationToken cancellationToken)
    {
        var traces = await db.TurnTraces.Where(trace => trace.OwnerId == ownerId && trace.CreatedAt < tracesBefore)
            .ExecuteDeleteAsync(cancellationToken);
        var signals = await db.LearningSignals
            .Where(signal => signal.OwnerId == ownerId && signal.CreatedAt < signalsBefore)
            .ExecuteDeleteAsync(cancellationToken);
        return traces + signals;
    }

    private static string? Limit(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
