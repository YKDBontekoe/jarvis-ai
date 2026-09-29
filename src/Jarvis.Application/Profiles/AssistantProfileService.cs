using Jarvis.Application.Files;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Domain.Profiles;

namespace Jarvis.Application.Profiles;

public sealed class AssistantProfileService(
    IAssistantProfileRepository profiles,
    ISkillRepository skills,
    IDocumentCollectionRepository collections,
    TimeProvider? clock = null) : IAssistantProfileService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<AssistantProfileRecord>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultAsync(ownerId, cancellationToken);
        return await profiles.ListAsync(ownerId, cancellationToken);
    }

    public async Task<AssistantProfileRecord> EnsureDefaultAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var existing = await profiles.GetDefaultAsync(ownerId, cancellationToken);
        if (existing is not null) return existing;
        return await profiles.CreateAsync(ownerId, DefaultDraft(), isDefault: true, cancellationToken);
    }

    public Task<AssistantProfileRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        profiles.GetAsync(id, ownerId, cancellationToken);

    public async Task<AssistantProfileRecord> CreateAsync(Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultAsync(ownerId, cancellationToken);
        var normalized = await NormalizeAsync(ownerId, draft, cancellationToken);
        var created = await profiles.CreateAsync(ownerId, normalized, isDefault: false, cancellationToken);
        if (normalized.IsDefault == true)
            await profiles.SetDefaultAsync(created.Id, ownerId, cancellationToken);
        return (await profiles.GetAsync(created.Id, ownerId, cancellationToken))!;
    }

    public async Task<AssistantProfileRecord?> UpdateAsync(Guid id, Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken)
    {
        var existing = await profiles.GetAsync(id, ownerId, cancellationToken);
        if (existing is null) return null;
        var normalized = await NormalizeAsync(ownerId, draft with { IsDefault = draft.IsDefault ?? existing.IsDefault },
            cancellationToken);
        var updated = await profiles.UpdateAsync(id, ownerId, normalized, cancellationToken);
        if (updated is null) return null;
        if (normalized.IsDefault == true)
            await profiles.SetDefaultAsync(id, ownerId, cancellationToken);
        return await profiles.GetAsync(id, ownerId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var existing = await profiles.GetAsync(id, ownerId, cancellationToken);
        if (existing is null) return false;
        if (existing.IsDefault)
            throw new ArgumentException("The default profile cannot be deleted.", nameof(id));
        return await profiles.DeleteAsync(id, ownerId, cancellationToken);
    }

    public async Task<ProfileBinding> CaptureBindingAsync(Guid ownerId, Guid? profileId,
        CancellationToken cancellationToken)
    {
        AssistantProfileRecord profile;
        if (profileId is { } id)
        {
            profile = await profiles.GetAsync(id, ownerId, cancellationToken)
                      ?? throw new ArgumentException("That assistant profile was not found.", nameof(profileId));
        }
        else
            profile = await EnsureDefaultAsync(ownerId, cancellationToken);

        var snapshot = AssistantProfileSnapshot.From(profile, _clock.GetUtcNow());
        return new ProfileBinding(profile.Id, profile.Version, ProfileJson.Serialize(snapshot), profile.Name);
    }

    public async Task<AssistantProfileSnapshot> ResolveSnapshotAsync(ProfileBinding? binding, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var snapshot = ProfileJson.Deserialize(binding?.SnapshotJson);
        if (snapshot is not null) return snapshot;
        try
        {
            var captured = await CaptureBindingAsync(ownerId, binding?.ProfileId, cancellationToken);
            return ProfileJson.Deserialize(captured.SnapshotJson)!;
        }
        catch (ArgumentException)
        {
            var captured = await CaptureBindingAsync(ownerId, null, cancellationToken);
            return ProfileJson.Deserialize(captured.SnapshotJson)!;
        }
    }

    public ProfileScopeChange CompareScope(AssistantProfileSnapshot current, AssistantProfileSnapshot next) =>
        ProfileScope.Compare(current, next);

    private async Task<AssistantProfileDraft> NormalizeAsync(Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken)
    {
        var name = draft.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > AssistantProfile.MaxNameLength)
            throw new ArgumentException($"Name must contain 1 to {AssistantProfile.MaxNameLength} characters.",
                nameof(draft));
        var description = NormalizeOptional(draft.Description, AssistantProfile.MaxDescriptionLength, "description");
        var instructions = NormalizeOptional(draft.PersonaInstructions, AssistantProfile.MaxPersonaInstructionsLength,
            "persona instructions");
        var preferredName = NormalizeOptional(draft.PreferredName, AssistantProfile.MaxPreferredNameLength,
            "preferred name");
        var replyLanguage = NormalizeOptional(draft.ReplyLanguage, AssistantProfile.MaxReplyLanguageLength,
            "reply language");
        var skillIds = DistinctIds(draft.EnabledSkillIds);
        if (skillIds.Length > 0)
        {
            var owned = await skills.ListAsync(ownerId, cancellationToken);
            var ownedIds = owned.Select(skill => skill.Id).ToHashSet();
            if (skillIds.Any(id => !ownedIds.Contains(id)))
                throw new ArgumentException("Every enabled skill must belong to the same owner.", nameof(draft));
        }

        var collectionIds = DistinctIds(draft.AllowedCollectionIds);
        if (collectionIds.Length > 0 &&
            !await collections.AllBelongToOwnerAsync(ownerId, collectionIds, cancellationToken))
            throw new ArgumentException("Every document collection must belong to the same owner.", nameof(draft));

        var memoryScope = draft.MemoryScope?.Trim().ToLowerInvariant() ?? MemoryScopes.All;
        if (!MemoryScopes.IsValid(memoryScope))
            throw new ArgumentException("Choose a memory scope of all, profile, or pinned.", nameof(draft));
        var modelClass = draft.ModelClass?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(modelClass)) modelClass = null;
        if (!ProfileModelClasses.IsValid(modelClass))
            throw new ArgumentException("Choose a model class of chat, fast, or reasoning.", nameof(draft));
        var chatModel = NormalizeModelId(draft.ChatModel, "chat model");
        var fastModel = NormalizeModelId(draft.FastModel, "fast model");
        var reasoning = draft.ReasoningEffort?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(reasoning)) reasoning = null;
        if (reasoning is not null)
        {
            try { _ = new ModelSettings(ModelSettings.Codex, ReasoningEffort: reasoning).Normalize(); }
            catch (ArgumentException)
            {
                throw new ArgumentException("Choose a supported reasoning effort for the selected model.", nameof(draft));
            }
        }

        return new AssistantProfileDraft(
            name,
            description,
            instructions,
            preferredName,
            replyLanguage,
            draft.IncludeOwnerPersona,
            draft.RestrictSkills,
            skillIds,
            draft.RestrictFiles,
            collectionIds,
            modelClass,
            chatModel,
            fastModel,
            reasoning,
            memoryScope,
            draft.IncludePinnedMemories,
            draft.ContributeToLearning,
            draft.AllowPersonaLearning,
            draft.AllowRemember,
            draft.IsDefault);
    }

    private static AssistantProfileDraft DefaultDraft() => new(
        AssistantProfile.DefaultName,
        AssistantProfile.DefaultDescription);

    private static Guid[] DistinctIds(IReadOnlyList<Guid>? ids) =>
        (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Keep {field} under {maxLength} characters.", nameof(value));
        return trimmed;
    }

    private static string? NormalizeModelId(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        try { return new ModelSettings(ModelSettings.Codex, ChatModel: trimmed).Normalize().ChatModel; }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(exception.Message.Replace("ChatModel", field, StringComparison.Ordinal),
                nameof(value));
        }
    }
}
