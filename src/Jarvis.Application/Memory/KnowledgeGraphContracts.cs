namespace Jarvis.Application.Memory;

public static class GraphEntityTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { "person", "place", "organization", "project", "thing", "event", "pet", "topic" };

    public static string Normalize(string? type)
    {
        var value = type?.Trim().ToLowerInvariant();
        return value is not null && All.Contains(value) ? value : "thing";
    }
}

public sealed record GraphEntityRecord(Guid Id, string Name, string Type, string? Summary,
    IReadOnlyList<string> Aliases, int RelationCount, DateTimeOffset UpdatedAt);

/// <summary>A fact with a validity interval. A null <see cref="ValidTo"/> means the fact is currently true.</summary>
public sealed record GraphRelationRecord(Guid Id, Guid SubjectId, string SubjectName, string Predicate,
    Guid? ObjectId, string? ObjectName, string? ObjectValue, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    float Confidence, Guid? SourceMemoryId);

public sealed record GraphEdge(Guid From, Guid To, string Predicate, float Confidence, DateTimeOffset ValidFrom);

/// <summary>A current fact whose object is a literal (a date, title, or short description) rather than another entity.</summary>
public sealed record GraphLiteral(Guid EntityId, string Predicate, string Value, float Confidence,
    DateTimeOffset ValidFrom);

public sealed record GraphOverview(IReadOnlyList<GraphEntityRecord> Entities, IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<GraphLiteral> Literals);

public sealed record GraphEntityDetails(GraphEntityRecord Entity, IReadOnlyList<GraphRelationRecord> Current,
    IReadOnlyList<GraphRelationRecord> History);

/// <summary>An extracted fact to merge into the graph.</summary>
public sealed record GraphFact(string Subject, string SubjectType, string Predicate, string Object, string? ObjectType,
    bool ObjectIsEntity, bool Exclusive, DateTimeOffset ValidFrom, float Confidence);

public interface IKnowledgeGraphRepository
{
    Task<IReadOnlyList<GraphEntityRecord>> ListEntitiesAsync(Guid ownerId, string? search, int limit,
        CancellationToken cancellationToken);
    Task<GraphEntityDetails?> GetEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken);
    Task<GraphEntityDetails?> FindEntityAsync(Guid ownerId, string name, DateTimeOffset? asOf,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<GraphEntityRecord>> FindMentionedAsync(Guid ownerId, string text, int limit,
        CancellationToken cancellationToken);
    Task<GraphOverview> GetOverviewAsync(Guid ownerId, int limit, CancellationToken cancellationToken);

    /// <summary>Merges facts; exclusive facts close the subject's other current values for the predicate.</summary>
    Task<int> MergeAsync(Guid ownerId, IReadOnlyList<GraphFact> facts, Guid? sourceMemoryId,
        CancellationToken cancellationToken);

    Task<GraphEntityRecord?> UpdateEntityAsync(Guid ownerId, Guid entityId, string? name, string? type,
        string? summary, CancellationToken cancellationToken);

    Task<bool> CloseRelationAsync(Guid ownerId, Guid relationId, CancellationToken cancellationToken);

    Task<bool> MergeEntitiesAsync(Guid ownerId, Guid keepId, Guid absorbId, CancellationToken cancellationToken);

    Task<bool> DeleteEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken);
}

public sealed record UpdateGraphEntityRequest(string? Name, string? Type, string? Summary);
public sealed record ProposeGraphFactRequest(string Subject, string? SubjectType, string Predicate, string Object,
    string? ObjectType, bool ObjectIsEntity = false, bool Exclusive = true);
public sealed record MergeGraphEntitiesRequest(Guid KeepId, Guid AbsorbId);

public static class GraphNames
{
    public const string UserKey = "user";

    /// <summary>Case- and punctuation-insensitive key used to de-duplicate entities.</summary>
    public static string Key(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Equals("me", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("user", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("the user", StringComparison.OrdinalIgnoreCase))
            return UserKey;
        return new string(trimmed.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    public static string Predicate(string predicate)
    {
        var normalized = string.Join('_', predicate.Trim().ToLowerInvariant()
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
        return new string(normalized.Where(character => char.IsLetterOrDigit(character) || character == '_')
            .Take(60).ToArray());
    }
}
