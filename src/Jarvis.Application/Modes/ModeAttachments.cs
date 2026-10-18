using Jarvis.Application.HomeRoutines;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Modes;

/// <summary>A profile and/or a home routine the owner prefers for a mode. Both are suggestions only.</summary>
public sealed record ModeAttachment(Guid? ProfileId, string? RoutineId);

public sealed class ModeAttachmentState
{
    public Dictionary<string, ModeAttachment> Items { get; set; } = [];
}

public sealed record AttachedProfile(Guid Id, string Name, ProfileScopeChange? ScopeChange, IReadOnlyList<string> Broadens);

public sealed record AttachedRoutine(string Id, string Title);

public sealed record ModeAttachmentView(string Mode, string Label, AttachedProfile? Profile, AttachedRoutine? Routine,
    string? Problem);

/// <summary>What the current mode suggests, with the plain-language difference between looking and doing.</summary>
public sealed record ModeSuggestion(string Mode, string Label, AttachedProfile? Profile, AttachedRoutine? Routine,
    IReadOnlyList<string> VisualOnly, IReadOnlyList<string> NeedsYou, string? Problem);

public static class ModeAttachmentRules
{
    public static readonly IReadOnlyList<string> VisualOnly =
    [
        "Jarvis shows this profile and routine as a suggestion while the mode is on."
    ];

    public static readonly IReadOnlyList<string> NeedsYou =
    [
        "Nothing switches profile by itself; a switch that widens access asks you first.",
        "A routine only previews what it would do. Running one is a step you take, and security controls always need your approval."
    ];
}

public interface IModeAttachmentService
{
    Task<IReadOnlyList<ModeAttachmentView>> ListAsync(Guid ownerId, Guid? currentProfileId, CancellationToken cancellationToken);
    Task<ModeOperation<ModeAttachmentView>> SetAsync(Guid ownerId, string mode, Guid? profileId, string? routineId,
        CancellationToken cancellationToken);
    Task<ModeSuggestion> SuggestAsync(Guid ownerId, Guid? currentProfileId, CancellationToken cancellationToken);
}

public sealed class ModeAttachmentService(IOwnerSettingsStore settings, IModeService modes, IAssistantProfileService profiles,
    TimeProvider? timeProvider = null) : IModeAttachmentService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<ModeAttachmentView>> ListAsync(Guid ownerId, Guid? currentProfileId,
        CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var owned = await profiles.ListAsync(ownerId, cancellationToken);
        return ModeIds.All.Where(state.Items.ContainsKey)
            .Select(mode => View(mode, state.Items[mode], owned, currentProfileId)).ToArray();
    }

    public async Task<ModeOperation<ModeAttachmentView>> SetAsync(Guid ownerId, string mode, Guid? profileId,
        string? routineId, CancellationToken cancellationToken)
    {
        var key = mode.Trim().ToLowerInvariant();
        if (!ModeIds.IsValid(key)) return ModeOperation<ModeAttachmentView>.Invalid("mode", "Unknown mode.");
        if (routineId is not null && HomeRoutineRules.Find(routineId) is null)
            return ModeOperation<ModeAttachmentView>.Invalid("routineId", "Choose morning, focus, leave or evening.");
        var owned = await profiles.ListAsync(ownerId, cancellationToken);
        if (profileId is { } id && owned.All(p => p.Id != id))
            return ModeOperation<ModeAttachmentView>.Invalid("profileId", "Choose one of your profiles.");

        var state = await LoadAsync(ownerId, cancellationToken);
        if (profileId is null && routineId is null) state.Items.Remove(key);
        else state.Items[key] = new ModeAttachment(profileId, routineId);
        await settings.SaveAsync(ownerId, SettingsSections.ModeAttachments, state, cancellationToken);
        return ModeOperation<ModeAttachmentView>.Ok(View(key, state.Items.GetValueOrDefault(key) ?? new ModeAttachment(null, null), owned, null));
    }

    public async Task<ModeSuggestion> SuggestAsync(Guid ownerId, Guid? currentProfileId, CancellationToken cancellationToken)
    {
        var current = (await modes.GetStateAsync(ownerId, cancellationToken)).Current.Mode;
        var label = ModeCatalog.Find(current).Label;
        var state = await LoadAsync(ownerId, cancellationToken);
        if (!state.Items.TryGetValue(current, out var attachment))
            return new ModeSuggestion(current, label, null, null, [], [], null);
        var view = View(current, attachment, await profiles.ListAsync(ownerId, cancellationToken), currentProfileId);
        return new ModeSuggestion(current, label, view.Profile, view.Routine, ModeAttachmentRules.VisualOnly,
            ModeAttachmentRules.NeedsYou, view.Problem);
    }

    private ModeAttachmentView View(string mode, ModeAttachment attachment, IReadOnlyList<AssistantProfileRecord> owned,
        Guid? currentProfileId)
    {
        AttachedProfile? profile = null;
        string? problem = null;
        if (attachment.ProfileId is { } id)
        {
            var found = owned.FirstOrDefault(p => p.Id == id);
            if (found is null) problem = "The attached profile was deleted. Choose another one.";
            else
            {
                var now = clock.GetUtcNow();
                var current = owned.FirstOrDefault(p => p.Id == currentProfileId) ?? owned.FirstOrDefault(p => p.IsDefault);
                var next = AssistantProfileSnapshot.From(found, now);
                var currentSnapshot = current is null ? null : AssistantProfileSnapshot.From(current, now);
                profile = new AttachedProfile(found.Id, found.Name,
                    currentSnapshot is null || current!.Id == found.Id ? null : ProfileScope.Compare(currentSnapshot, next),
                    currentSnapshot is null ? [] : ProfileGuidance.Broadens(currentSnapshot, next));
            }
        }
        var routine = attachment.RoutineId is { } routineId && HomeRoutineRules.Find(routineId) is { } template
            ? new AttachedRoutine(template.Id, template.Title)
            : null;
        return new ModeAttachmentView(mode, ModeCatalog.Find(mode).Label, profile, routine, problem);
    }

    private async Task<ModeAttachmentState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<ModeAttachmentState>(ownerId, SettingsSections.ModeAttachments, cancellationToken)
        ?? new ModeAttachmentState();
}
