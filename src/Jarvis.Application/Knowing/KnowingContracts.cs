using Jarvis.Domain.Knowing;

namespace Jarvis.Application.Knowing;

public static class MemoryVerdicts
{
    /// <summary>Jarvis has it right; the memory is kept and its confidence raised.</summary>
    public const string Right = "right";

    /// <summary>It was never true; the memory is deleted.</summary>
    public const string Wrong = "wrong";

    /// <summary>It used to be true; the memory is ended, so it stays in history.</summary>
    public const string Outdated = "outdated";

    public static bool IsValid(string? verdict) => verdict is Right or Wrong or Outdated;
}

public static class KnowingRules
{
    public const int DeckSize = 10;
    public const int DailyGoal = 5;

    /// <summary>A memory answered recently is not asked about again for this long.</summary>
    public const int ReviewCooldownDays = 90;

    public const float ConfirmedConfidence = 0.95f;
    public const int SnapshotLength = 300;

    /// <summary>Kinds the owner wrote themselves (or that only mirror what they wrote) are never quizzed.</summary>
    public static readonly IReadOnlySet<string> SkippedKinds =
        new HashSet<string>(StringComparer.Ordinal) { "journal" };
}

// ---- Train Jarvis ----

public sealed record TrainingCard(Guid MemoryId, string Kind, string Content, int ConfidencePercent,
    DateTimeOffset LearnedAt);

public sealed record TrainingStats(
    int Reviewed,
    int Right,
    int Wrong,
    int Outdated,
    int? AccuracyPercent,
    int Today,
    int DailyGoal,
    int StreakDays,
    int ReviewableLeft);

public sealed record TrainingDeck(IReadOnlyList<TrainingCard> Cards, TrainingStats Stats);

public sealed record TrainingAnswer(string Verdict, TrainingStats Stats);

// ---- Map of the unknown ----

/// <summary>Something Jarvis does not know yet about an entity it knows: one patch of fog.</summary>
public sealed record KnowledgeGap(Guid EntityId, string EntityName, string EntityType, string Predicate,
    string Question);

public sealed record GapEntity(Guid Id, string Name, string Type, int Known, int Expected);

public sealed record KnowledgeMap(
    int FogPercent,
    int Known,
    int Expected,
    KnowledgeGap? Question,
    IReadOnlyList<GapEntity> Entities,
    int AnsweredToday);

public sealed record GapAnswerResult(bool Saved, KnowledgeMap Map);

/// <summary>Gap keys ("entityId:predicate") the owner said they do not know or did not want asked.</summary>
public sealed record KnowingState(IReadOnlyList<string>? SkippedGaps = null, DateOnly? LastAnsweredOn = null,
    int AnsweredOnLastDay = 0);

// ---- Trust ladder ----

public static class TrustLevels
{
    public const string New = "new";
    public const string Learning = "learning";
    public const string Trusted = "trusted";

    /// <summary>The owner handed this kind of action over: Jarvis does it without asking.</summary>
    public const string HandsFree = "hands_free";
}

public sealed record TrustRung(
    string Category,
    string Label,
    string Level,
    int Streak,
    int Approved,
    int Declined,
    int NextAt,
    bool CanHandOver,
    DateTimeOffset? LastDecidedAt,
    DateTimeOffset? HandedOverUntil);

public sealed record DecidedApproval(string ToolName, string ArgumentsJson, bool Approved, DateTimeOffset DecidedAt);

/// <summary>Approvals the owner already decided, newest last.</summary>
public interface IApprovalHistory
{
    Task<IReadOnlyList<DecidedApproval>> ListDecidedAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken);
}

public enum TrustHandOverResult
{
    HandedOver,
    NotFound,
    NotEarned,
    NotAllowed
}

// ---- Special chats ----

public static class ChatModeKinds
{
    /// <summary>Jarvis pretends to know nothing and interviews the owner; what changed updates memory.</summary>
    public const string Stranger = "stranger";

    public const string Rehearse = "rehearse";
    public const string PastSelf = "past_self";
    public const string Council = "council";

    public static readonly IReadOnlyList<string> All = [Stranger, Rehearse, PastSelf, Council];
}

/// <summary>What the owner chose when starting a special chat. Unused fields stay null.</summary>
public sealed record ChatModeRequest(
    string Kind,
    Guid? PersonId = null,
    string? Scenario = null,
    string? Goal = null,
    DateOnly? AsOf = null,
    string? Question = null,
    IReadOnlyList<string>? Advisers = null);

public sealed record ChatModeStarted(Guid ConversationId, string Kind, string Title, string Opening);

public sealed record ChatModeView(Guid ConversationId, string Kind, string Title, DateTimeOffset CreatedAt);

public sealed record ChatModeOperation(ChatModeStarted? Value, string? Field = null, string? Error = null)
{
    public static ChatModeOperation Invalid(string field, string error) => new(null, field, error);
}

public interface IKnowingRepository
{
    Task AddReviewAsync(MemoryReview review, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryReview>> ListReviewsAsync(Guid ownerId, CancellationToken cancellationToken);

    Task AddChatModeAsync(ChatMode mode, CancellationToken cancellationToken);
    Task<ChatMode?> GetChatModeAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMode>> ListChatModesAsync(Guid ownerId, CancellationToken cancellationToken);
}

public interface ITrainingService
{
    Task<TrainingDeck> DeckAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <returns>Null when the memory does not exist (or is not the owner's).</returns>
    /// <exception cref="ArgumentException">The verdict is not right, wrong or outdated.</exception>
    Task<TrainingAnswer?> AnswerAsync(Guid ownerId, Guid memoryId, string verdict, CancellationToken cancellationToken);
}

public interface IKnowledgeMapService
{
    Task<KnowledgeMap> MapAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Stores the owner's answer as a fact in the knowledge graph.</summary>
    /// <returns>Null when the entity is unknown or the gap is not open.</returns>
    Task<GapAnswerResult?> AnswerAsync(Guid ownerId, Guid entityId, string predicate, string answer,
        CancellationToken cancellationToken);

    /// <summary>"I don't know" or "don't ask": the gap is never asked again.</summary>
    Task<KnowledgeMap> SkipAsync(Guid ownerId, Guid entityId, string predicate, CancellationToken cancellationToken);
}

public interface ITrustLadderService
{
    Task<IReadOnlyList<TrustRung>> LadderAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Hands an earned category over for 90 days through the owner's standing approvals.</summary>
    Task<TrustHandOverResult> HandOverAsync(Guid ownerId, string category, CancellationToken cancellationToken);

    Task<bool> TakeBackAsync(Guid ownerId, string category, CancellationToken cancellationToken);
}

public interface IChatModeService
{
    Task<ChatModeOperation> StartAsync(Guid ownerId, ChatModeRequest request, CancellationToken cancellationToken);
    Task<ChatMode?> GetAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatModeView>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
}
