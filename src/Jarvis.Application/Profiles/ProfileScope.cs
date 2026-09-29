using Jarvis.Application.Settings;
using Jarvis.Domain.Memory;
using Jarvis.Domain.Profiles;

namespace Jarvis.Application.Profiles;

/// <summary>Pure helpers for applying a bound profile snapshot to recall, tools, and learning.</summary>
public static class ProfileScope
{
    public static bool AllowsSkill(AssistantProfileSnapshot? snapshot, Guid skillId) =>
        snapshot is null || !snapshot.RestrictSkills || snapshot.EnabledSkillIds.Contains(skillId);

    public static bool AllowsFile(AssistantProfileSnapshot? snapshot, Guid fileId, IReadOnlySet<Guid> allowedFileIds) =>
        snapshot is null || !snapshot.RestrictFiles || allowedFileIds.Contains(fileId);

    public static bool AllowsMemory(AssistantProfileSnapshot? snapshot, MemoryRecord memory)
    {
        if (snapshot is null) return true;
        var scope = snapshot.MemoryScope;
        if (scope == MemoryScopes.All) return true;
        if (memory.IsPinned && snapshot.IncludePinnedMemories) return true;
        if (scope == MemoryScopes.Pinned) return false;
        return memory.ProfileId == snapshot.ProfileId;
    }

    public static bool AllowsPersonaLearning(AssistantProfileSnapshot? snapshot, LearningSettings owner) =>
        owner.LearnPersona && (snapshot?.AllowPersonaLearning ?? true);

    public static bool AllowsRemember(AssistantProfileSnapshot? snapshot) =>
        snapshot?.AllowRemember ?? true;

    public static bool ContributesToLearning(AssistantProfileSnapshot? snapshot) =>
        snapshot?.ContributeToLearning ?? true;

    public static bool ContributesToLearning(string? snapshotJson) =>
        ContributesToLearning(ProfileJson.Deserialize(snapshotJson));

    public static string KnowledgeFingerprint(AssistantProfileSnapshot snapshot)
    {
        var skills = snapshot.RestrictSkills
            ? string.Join(',', snapshot.EnabledSkillIds.OrderBy(id => id))
            : "*";
        var files = snapshot.RestrictFiles
            ? string.Join(',', snapshot.AllowedCollectionIds.OrderBy(id => id))
            : "*";
        return $"skills:{skills}|files:{files}|memory:{snapshot.MemoryScope}|pinned:{snapshot.IncludePinnedMemories}";
    }

    public static ProfileScopeChange Compare(AssistantProfileSnapshot current, AssistantProfileSnapshot next)
    {
        var details = new List<string>();
        var skillsChanged = SkillKey(current) != SkillKey(next);
        if (skillsChanged)
            details.Add("Enabled skills for this profile are different.");
        var filesChanged = FileKey(current) != FileKey(next);
        if (filesChanged)
            details.Add("Searchable document collections are different.");
        var memoryChanged = current.MemoryScope != next.MemoryScope ||
                            current.IncludePinnedMemories != next.IncludePinnedMemories;
        if (memoryChanged)
            details.Add("Memory recall scope is different.");
        return new ProfileScopeChange(skillsChanged, filesChanged, memoryChanged, details);
    }

    public static ModelSettings OverlayModels(ModelSettings owner, AssistantProfileSnapshot? snapshot)
    {
        if (snapshot is null) return owner;
        return owner with
        {
            ChatModel = string.IsNullOrWhiteSpace(snapshot.ChatModel) ? owner.ChatModel : snapshot.ChatModel,
            FastModel = string.IsNullOrWhiteSpace(snapshot.FastModel) ? owner.FastModel : snapshot.FastModel,
            ReasoningEffort = string.IsNullOrWhiteSpace(snapshot.ReasoningEffort)
                ? owner.ReasoningEffort
                : snapshot.ReasoningEffort
        };
    }

    public static string? ResolveModelClass(AssistantProfileSnapshot? snapshot) => snapshot?.ModelClass;

    private static string SkillKey(AssistantProfileSnapshot snapshot) =>
        snapshot.RestrictSkills ? string.Join(',', snapshot.EnabledSkillIds.OrderBy(id => id)) : "*";

    private static string FileKey(AssistantProfileSnapshot snapshot) =>
        snapshot.RestrictFiles ? string.Join(',', snapshot.AllowedCollectionIds.OrderBy(id => id)) : "*";
}
