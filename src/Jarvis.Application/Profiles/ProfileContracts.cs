using Jarvis.Domain.Profiles;

namespace Jarvis.Application.Profiles;

public sealed record AssistantProfileRecord(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Description,
    bool IsDefault,
    int Version,
    string? PersonaInstructions,
    string? PreferredName,
    string? ReplyLanguage,
    bool IncludeOwnerPersona,
    bool RestrictSkills,
    IReadOnlyList<Guid> EnabledSkillIds,
    bool RestrictFiles,
    IReadOnlyList<Guid> AllowedCollectionIds,
    string? ModelClass,
    string? ChatModel,
    string? FastModel,
    string? ReasoningEffort,
    string MemoryScope,
    bool IncludePinnedMemories,
    bool ContributeToLearning,
    bool AllowPersonaLearning,
    bool AllowRemember,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Immutable copy of a profile used by a conversation or task after it is bound.</summary>
public sealed record AssistantProfileSnapshot(
    Guid ProfileId,
    int Version,
    string Name,
    string? Description,
    string? PersonaInstructions,
    string? PreferredName,
    string? ReplyLanguage,
    bool IncludeOwnerPersona,
    bool RestrictSkills,
    IReadOnlyList<Guid> EnabledSkillIds,
    bool RestrictFiles,
    IReadOnlyList<Guid> AllowedCollectionIds,
    string? ModelClass,
    string? ChatModel,
    string? FastModel,
    string? ReasoningEffort,
    string MemoryScope,
    bool IncludePinnedMemories,
    bool ContributeToLearning,
    bool AllowPersonaLearning,
    bool AllowRemember,
    DateTimeOffset CapturedAt)
{
    public static AssistantProfileSnapshot From(AssistantProfileRecord profile, DateTimeOffset capturedAt) => new(
        profile.Id,
        profile.Version,
        profile.Name,
        profile.Description,
        profile.PersonaInstructions,
        profile.PreferredName,
        profile.ReplyLanguage,
        profile.IncludeOwnerPersona,
        profile.RestrictSkills,
        profile.EnabledSkillIds,
        profile.RestrictFiles,
        profile.AllowedCollectionIds,
        profile.ModelClass,
        profile.ChatModel,
        profile.FastModel,
        profile.ReasoningEffort,
        profile.MemoryScope,
        profile.IncludePinnedMemories,
        profile.ContributeToLearning,
        profile.AllowPersonaLearning,
        profile.AllowRemember,
        capturedAt);
}

public sealed record AssistantProfileDraft(
    string Name,
    string? Description = null,
    string? PersonaInstructions = null,
    string? PreferredName = null,
    string? ReplyLanguage = null,
    bool IncludeOwnerPersona = true,
    bool RestrictSkills = false,
    IReadOnlyList<Guid>? EnabledSkillIds = null,
    bool RestrictFiles = false,
    IReadOnlyList<Guid>? AllowedCollectionIds = null,
    string? ModelClass = null,
    string? ChatModel = null,
    string? FastModel = null,
    string? ReasoningEffort = null,
    string MemoryScope = MemoryScopes.All,
    bool IncludePinnedMemories = true,
    bool ContributeToLearning = true,
    bool AllowPersonaLearning = true,
    bool AllowRemember = true,
    bool? IsDefault = null);

public sealed record ProfileBinding(Guid ProfileId, int Version, string SnapshotJson, string Name);

public sealed record ProfileScopeChange(
    bool SkillsChanged,
    bool FilesChanged,
    bool MemoryChanged,
    IReadOnlyList<string> Details)
{
    public bool RequiresConfirmation => SkillsChanged || FilesChanged || MemoryChanged;
}

public sealed record ConversationProfileState(
    Guid? ProfileId,
    int? ProfileVersion,
    string? ProfileName,
    bool ProfileDeleted,
    AssistantProfileSnapshot? Snapshot);

public interface IAssistantProfileRepository
{
    Task<IReadOnlyList<AssistantProfileRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<AssistantProfileRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AssistantProfileRecord?> GetDefaultAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<AssistantProfileRecord> CreateAsync(Guid ownerId, AssistantProfileDraft draft, bool isDefault,
        CancellationToken cancellationToken);
    Task<AssistantProfileRecord?> UpdateAsync(Guid id, Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task SetDefaultAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IAssistantProfileService
{
    Task<IReadOnlyList<AssistantProfileRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<AssistantProfileRecord> EnsureDefaultAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<AssistantProfileRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AssistantProfileRecord> CreateAsync(Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken);
    Task<AssistantProfileRecord?> UpdateAsync(Guid id, Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<ProfileBinding> CaptureBindingAsync(Guid ownerId, Guid? profileId, CancellationToken cancellationToken);
    Task<AssistantProfileSnapshot> ResolveSnapshotAsync(ProfileBinding? binding, Guid ownerId,
        CancellationToken cancellationToken);
    ProfileScopeChange CompareScope(AssistantProfileSnapshot current, AssistantProfileSnapshot next);
}

public static class ProfileJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static string Serialize(AssistantProfileSnapshot snapshot) =>
        System.Text.Json.JsonSerializer.Serialize(snapshot, Options);

    public static AssistantProfileSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AssistantProfileSnapshot>(json, Options);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
