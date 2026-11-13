namespace Jarvis.Domain.Knowing;

/// <summary>
/// The owner's answer to one "Train Jarvis" card: whether something Jarvis believed about them is right, wrong or no
/// longer true. Kept after a wrong memory is deleted, so the accuracy score keeps counting it. Only a short snapshot of
/// the memory is stored, never more than the card showed.
/// </summary>
public sealed record MemoryReview(
    Guid Id,
    Guid OwnerId,
    Guid MemoryId,
    string Verdict,
    string Snapshot,
    DateTimeOffset ReviewedAt);

/// <summary>
/// A conversation started as a special kind of chat (the stranger's interview, a rehearsal, past you, the council).
/// <see cref="PayloadJson"/> holds what the owner chose when starting it; the agent reads it as untrusted owner text.
/// </summary>
public sealed record ChatMode(
    Guid ConversationId,
    Guid OwnerId,
    string Kind,
    string PayloadJson,
    DateTimeOffset CreatedAt);
