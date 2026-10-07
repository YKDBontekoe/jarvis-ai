using System.Security.Cryptography;
using System.Text;
using Jarvis.Domain.Improvements;

namespace Jarvis.Application.Improvements;

public static class ImprovementKinds
{
    public const string Memory = "memory";
    public const string Skill = "skill";
    public const string Review = "review";

    public static bool IsValid(string? kind) => kind is Memory or Skill or Review;
}

/// <summary>
/// <c>pending</c> waits for the owner; <c>accepted</c> was applied by the owner; <c>applied</c> was applied by Jarvis on
/// its own (low-risk memories only) and can be undone; <c>dismissed</c> and <c>undone</c> are the owner's refusals and
/// are never offered again.
/// </summary>
public static class ImprovementStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Applied = "applied";
    public const string Dismissed = "dismissed";
    public const string Undone = "undone";

    public static bool IsValid(string? status) =>
        status is Pending or Accepted or Applied or Dismissed or Undone;

    /// <summary>A refusal by the owner: the fingerprint stays closed.</summary>
    public static bool IsRefusal(string status) => status is Dismissed or Undone;

    /// <summary>The change is in force and undo can reverse it.</summary>
    public static bool CanUndo(string status) => status is Accepted or Applied;
}

public static class ImprovementRules
{
    public static readonly TimeSpan NotifyInterval = TimeSpan.FromDays(7);
    public static readonly TimeSpan UndoWindow = TimeSpan.FromDays(30);
    public const string NotificationType = "improvement.suggested";

    /// <summary>A memory candidate below this is not worth even asking about.</summary>
    public const float MemoryReviewMinConfidence = 0.6f;

    /// <summary>
    /// At or above this, and with the owner's AutoApplyLowRiskMemory on, a memory is saved straight away. This is the
    /// threshold reflection and dreaming already used to save memories on their own.
    /// </summary>
    public const float MemoryAutoApplyMinConfidence = 0.8f;

    public const int MaxPending = 30;

    public static string MemoryFingerprint(string kind, string content) =>
        "mem:" + Hash(kind.Trim().ToLowerInvariant() + "|" + Normalize(content));

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    public static string Normalize(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>One thing a miner or a learning pass would like to offer.</summary>
public sealed record ProposalCandidate(string Kind, string Fingerprint, string Title, string Evidence,
    double Confidence, string PayloadJson);

/// <summary>What accepting a memory proposal stores.</summary>
public sealed record MemoryPayload(string Kind, string Content, float Importance, float Confidence, Guid? ProfileId,
    Guid? SourceId);

/// <summary>What accepting a skill proposal saves. It is validated again on accept.</summary>
public sealed record SkillPayload(string Name, string Description, string Instructions);

/// <summary>
/// What accepting a review proposal does: <c>disable_skill</c> turns off the named skill, <c>note</c> only records that
/// the owner has seen it.
/// </summary>
public sealed record ReviewPayload(string Action, string? Target);

public static class ReviewActions
{
    public const string DisableSkill = "disable_skill";
    public const string Note = "note";
}

public sealed record ImprovementView(Guid Id, string Kind, string Title, string Evidence, double Confidence,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public bool CanUndo => ImprovementStatuses.CanUndo(Status);
}

/// <summary>How a memory candidate ended up.</summary>
public enum MemoryOutcome
{
    /// <summary>Saved on the owner's behalf and recorded so it can be undone.</summary>
    Saved,

    /// <summary>Waiting for the owner to accept.</summary>
    Proposed,

    /// <summary>Not stored: too uncertain, already known, or the owner turned this one down before.</summary>
    Dropped
}

public sealed record MemoryCandidate(string Kind, string Content, float Importance, float Confidence, Guid? ProfileId,
    Guid? SourceId, string Evidence);

public sealed record ImprovementState(DateTimeOffset? LastNotifiedAt);

public interface IImprovementRepository
{
    Task<IReadOnlyList<ImprovementProposal>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ImprovementProposal?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<ImprovementProposal?> FindByFingerprintAsync(Guid ownerId, string fingerprint,
        CancellationToken cancellationToken);
    Task AddAsync(ImprovementProposal proposal, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken);
    Task<int> DeleteManyAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public interface IImprovementService
{
    /// <summary>Proposals waiting for a decision, strongest first, then recent changes that can still be undone.</summary>
    Task<IReadOnlyList<ImprovementView>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the pending proposals whose fingerprint starts with <paramref name="fingerprintPrefix"/> in line with
    /// <paramref name="candidates"/>: new ones are added, current ones refreshed, ones that no longer show up removed.
    /// Decided proposals are never touched or re-added. Returns how many new proposals were added.
    /// </summary>
    Task<int> SyncAsync(Guid ownerId, string fingerprintPrefix, IReadOnlyList<ProposalCandidate> candidates,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a memory a learning pass found, or proposes it, according to its confidence and the owner's settings.
    /// A candidate the owner already declined or undid is dropped, so nothing is saved twice or against their wish.
    /// </summary>
    Task<MemoryOutcome> SaveOrProposeMemoryAsync(Guid ownerId, MemoryCandidate candidate, bool autoApply,
        CancellationToken cancellationToken);

    Task<ImprovementView?> AcceptAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DismissAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> UndoAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}
