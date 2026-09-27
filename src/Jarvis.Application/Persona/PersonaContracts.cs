namespace Jarvis.Application.Persona;

public static class PersonaCategories
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { "tone", "format", "language", "workstyle", "boundaries", "schedule", "other" };

    public static string Normalize(string? category)
    {
        var value = category?.Trim().ToLowerInvariant();
        return value is not null && All.Contains(value) ? value : "other";
    }
}

/// <summary>One learned or user-stated rule about how the owner wants Jarvis to work.</summary>
public sealed record PersonaTrait(
    Guid Id,
    string Category,
    string Statement,
    float Confidence,
    int Evidence,
    string Source,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PersonaProfile(
    string? CustomInstructions = null,
    string? PreferredName = null,
    string? ReplyLanguage = null,
    IReadOnlyList<PersonaTrait>? Traits = null,
    DateTimeOffset? LastReflectedAt = null)
{
    public static PersonaProfile Empty { get; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<PersonaTrait> TraitList => Traits ?? [];
}

/// <summary>A learned observation proposed by the agent or by background reflection.</summary>
public sealed record PersonaObservation(string Category, string Statement, float Confidence, Guid? ReplacesTraitId = null);

public sealed record MessageFeedbackRecord(Guid Id, Guid ConversationId, Guid MessageId, string Rating, string? Note,
    string? MessageExcerpt, DateTimeOffset CreatedAt);

public interface IMessageFeedbackRepository
{
    Task<MessageFeedbackRecord> SaveAsync(Guid ownerId, Guid conversationId, Guid messageId, string rating,
        string? note, CancellationToken cancellationToken);
    Task<IReadOnlyList<MessageFeedbackRecord>> ListUnprocessedAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
