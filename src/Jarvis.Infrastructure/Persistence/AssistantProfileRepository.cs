using Jarvis.Application.Profiles;
using Jarvis.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class AssistantProfileRepository(JarvisDbContext db) : IAssistantProfileRepository
{
    public async Task<IReadOnlyList<AssistantProfileRecord>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.AssistantProfiles.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken)).Select(profile => ToRecord(profile)!).ToArray();

    public async Task<AssistantProfileRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        ToRecord(await db.AssistantProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken));

    public async Task<AssistantProfileRecord?> GetDefaultAsync(Guid ownerId, CancellationToken cancellationToken) =>
        ToRecord(await db.AssistantProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.IsDefault, cancellationToken));

    public async Task<AssistantProfileRecord> CreateAsync(Guid ownerId, AssistantProfileDraft draft, bool isDefault,
        CancellationToken cancellationToken)
    {
        if (await db.AssistantProfiles.CountAsync(x => x.OwnerId == ownerId, cancellationToken) >=
            AssistantProfile.MaxProfilesPerOwner)
            throw new ArgumentException(
                $"Keep at most {AssistantProfile.MaxProfilesPerOwner} profiles; delete unused ones first.");
        if (await db.AssistantProfiles.AnyAsync(x => x.OwnerId == ownerId && x.Name == draft.Name, cancellationToken))
            throw new ArgumentException($"A profile named {draft.Name} already exists.");

        var profile = new AssistantProfile(ownerId, draft.Name, isDefault);
        Apply(profile, draft, incrementVersion: false);
        db.AssistantProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(profile)!;
    }

    public async Task<AssistantProfileRecord?> UpdateAsync(Guid id, Guid ownerId, AssistantProfileDraft draft,
        CancellationToken cancellationToken)
    {
        var profile = await db.AssistantProfiles.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (profile is null) return null;
        if (await db.AssistantProfiles.AnyAsync(
                x => x.OwnerId == ownerId && x.Name == draft.Name && x.Id != id, cancellationToken))
            throw new ArgumentException($"A profile named {draft.Name} already exists.");
        Apply(profile, draft, incrementVersion: true);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(profile);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var profile = await db.AssistantProfiles.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (profile is null) return false;
        db.AssistantProfiles.Remove(profile);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SetDefaultAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var profiles = await db.AssistantProfiles.Where(x => x.OwnerId == ownerId).ToListAsync(cancellationToken);
        var selected = profiles.SingleOrDefault(item => item.Id == id)
                       ?? throw new ArgumentException("That assistant profile was not found.", nameof(id));
        foreach (var profile in profiles)
            profile.MarkDefault(profile.Id == selected.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(AssistantProfile profile, AssistantProfileDraft draft, bool incrementVersion) =>
        profile.Apply(
            draft.Name,
            draft.Description,
            draft.PersonaInstructions,
            draft.PreferredName,
            draft.ReplyLanguage,
            draft.IncludeOwnerPersona,
            draft.RestrictSkills,
            draft.EnabledSkillIds?.ToArray() ?? [],
            draft.RestrictFiles,
            draft.AllowedCollectionIds?.ToArray() ?? [],
            draft.ModelClass,
            draft.ChatModel,
            draft.FastModel,
            draft.ReasoningEffort,
            draft.MemoryScope,
            draft.IncludePinnedMemories,
            draft.ContributeToLearning,
            draft.AllowPersonaLearning,
            draft.AllowRemember,
            incrementVersion);

    private static AssistantProfileRecord? ToRecord(AssistantProfile? profile) =>
        profile is null ? null : new(
            profile.Id, profile.OwnerId, profile.Name, profile.Description, profile.IsDefault, profile.Version,
            profile.PersonaInstructions, profile.PreferredName, profile.ReplyLanguage, profile.IncludeOwnerPersona,
            profile.RestrictSkills, profile.EnabledSkillIds, profile.RestrictFiles, profile.AllowedCollectionIds,
            profile.ModelClass, profile.ChatModel, profile.FastModel, profile.ReasoningEffort, profile.MemoryScope,
            profile.IncludePinnedMemories, profile.ContributeToLearning, profile.AllowPersonaLearning,
            profile.AllowRemember, profile.CreatedAt, profile.UpdatedAt);
}
