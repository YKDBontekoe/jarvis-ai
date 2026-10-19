namespace Jarvis.Application.Skills;

public static class SkillSources
{
    public const string Learned = "learned";
    public const string User = "user";
    public const string Imported = "imported";
}

public static class SkillStatuses
{
    public const string Active = "active";
    public const string Proposed = "proposed";
    public const string Disabled = "disabled";

    public static bool IsValid(string? status) => status is Active or Proposed or Disabled;
}

public sealed record SkillRecord(Guid Id, Guid OwnerId, string Name, string Description, string Instructions,
    string Source, string Status, bool IsLocked, int Version, int UseCount, DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int OutcomeRuns = 0, int OutcomeSuccesses = 0,
    DateTimeOffset? OutcomeCountedAt = null);

public sealed record SkillRevisionRecord(int Version, string Description, string Instructions, string Source,
    string? ChangeNote, DateTimeOffset CreatedAt);

public sealed record SkillDraft(string Name, string Description, string Instructions);

public interface ISkillRepository
{
    Task<IReadOnlyList<SkillRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<SkillRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<SkillRecord?> FindByNameAsync(Guid ownerId, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<SkillRevisionRecord>> ListRevisionsAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a skill or appends a new revision when the name exists. Returns null when the name is locked. A learned
    /// change to an active skill returns it to <c>proposed</c> unless <paramref name="statusForNew"/> is active, so the
    /// owner's review setting also covers rewrites.
    /// </summary>
    Task<SkillRecord?> UpsertAsync(Guid ownerId, SkillDraft draft, string source, string statusForNew,
        bool respectLock, string? changeNote, CancellationToken cancellationToken);

    Task<SkillRecord?> SetStatusAsync(Guid id, Guid ownerId, string status, CancellationToken cancellationToken);
    Task<SkillRecord?> SetLockedAsync(Guid id, Guid ownerId, bool locked, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task RecordUseAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Adds counted runs and successes to skills' current versions. Unknown ids are ignored.</summary>
    Task AddOutcomesAsync(Guid ownerId, IReadOnlyDictionary<Guid, SkillOutcomeDelta> deltas,
        CancellationToken cancellationToken);
}
