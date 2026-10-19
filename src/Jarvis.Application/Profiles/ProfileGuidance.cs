using Jarvis.Domain.Profiles;

namespace Jarvis.Application.Profiles;

/// <summary>
/// A ready-made working style. It fills in the persona and memory choices only; it never restricts to files or skills
/// the owner has not picked, and <see cref="NeedsSources"/> says when the owner still has to choose them.
/// </summary>
public sealed record ProfilePreset(string Id, string Name, string Description, IReadOnlyList<string> Keywords,
    AssistantProfileDraft Draft, bool NeedsSources);

public sealed record ProfileScopeSummary(
    string Name,
    int Version,
    string Skills,
    string Files,
    string Memory,
    string Language,
    string Learning,
    IReadOnlyList<string> Limits,
    string SnapshotNote);

public sealed record ProfileSuggestion(
    string Kind,
    Guid? ProfileId,
    string? PresetId,
    string? Name,
    string Reason,
    ProfileScopeChange? ScopeChange,
    IReadOnlyList<string> Broadens,
    bool RequiresConfirmation,
    string Note);

public static class SuggestionKinds
{
    public const string Existing = "existing";
    public const string Preset = "preset";
    public const string None = "none";
}

public static class ProfileGuidance
{
    public const int MaxTextLength = 500;

    public static IReadOnlyList<ProfilePreset> Presets { get; } =
    [
        new("planning", "Planning partner", "Helps you decide what to do next and when.",
            ["plan", "planning", "schedule", "prioritise", "prioritize", "week", "agenda", "next step", "plannen", "planning", "agenda"],
            new("Planning partner", "Helps you decide what to do next and when.",
                "Help me decide what to do next and when. Be concise, propose one clear next action, and say what you are assuming about my time.",
                MemoryScope: MemoryScopes.All), false),
        new("research", "Research analyst", "Answers from the sources you choose and says what is unknown.",
            ["research", "sources", "compare", "evidence", "paper", "study", "onderzoek", "bronnen", "vergelijk"],
            new("Research analyst", "Answers from the sources you choose and says what is unknown.",
                "Answer from the sources I point you to. Say which source each claim comes from and what the sources do not cover. Do not fill gaps from memory.",
                MemoryScope: MemoryScopes.Pinned, AllowRemember: false), true),
        new("writing", "Writing editor", "Tightens your writing without changing what you mean.",
            ["write", "writing", "edit", "draft", "rewrite", "email", "essay", "tone", "schrijf", "schrijven", "herschrijf", "mail"],
            new("Writing editor", "Tightens your writing without changing what you mean.",
                "Edit what I write: keep my meaning and voice, cut filler, and show what you changed. Ask before adding facts.",
                MemoryScope: MemoryScopes.Profile, AllowRemember: false), false),
        new("tutor", "Tutor", "Explains step by step and checks understanding.",
            ["learn", "study", "explain", "teach", "exam", "practice", "leren", "uitleg", "oefenen", "toets"],
            new("Tutor", "Explains step by step and checks understanding.",
                "Teach me step by step. Explain simply, ask one short question to check I understood, and say when something is uncertain.",
                MemoryScope: MemoryScopes.Profile), false),
        new("operator", "Project operator", "Keeps one project moving: status, blockers, next actions.",
            ["project", "status", "blocker", "milestone", "deadline", "update", "launch", "voortgang", "oplevering", "projectupdate"],
            new("Project operator", "Keeps one project moving: status, blockers, next actions.",
                "Keep this project moving: state where it stands, what blocks it and the next action for each blocker. Be brief and factual.",
                MemoryScope: MemoryScopes.All), true)
    ];

    public static ProfilePreset? FindPreset(string id) => Presets.FirstOrDefault(p => p.Id == id);

    public static ProfileScopeSummary Summarise(AssistantProfileRecord profile)
    {
        var limits = new List<string>();
        var skills = profile.RestrictSkills
            ? profile.EnabledSkillIds.Count == 0 ? "No skills" : Plural(profile.EnabledSkillIds.Count, "skill")
            : "All your skills";
        var files = profile.RestrictFiles
            ? profile.AllowedCollectionIds.Count == 0 ? "No documents" : Plural(profile.AllowedCollectionIds.Count, "document collection")
            : "All your documents";
        if (profile.RestrictSkills) limits.Add(profile.EnabledSkillIds.Count == 0
            ? "Cannot use any of your saved skills."
            : "Can only use the skills chosen for this profile.");
        if (profile.RestrictFiles) limits.Add(profile.AllowedCollectionIds.Count == 0
            ? "Cannot search any of your documents."
            : "Can only search the document collections chosen for this profile.");
        var memory = profile.MemoryScope switch
        {
            MemoryScopes.Pinned => profile.IncludePinnedMemories ? "Only pinned memories" : "No memories",
            MemoryScopes.Profile => profile.IncludePinnedMemories ? "Memories made in this profile, plus pinned" : "Only memories made in this profile",
            _ => "All your memories"
        };
        if (profile.MemoryScope != MemoryScopes.All) limits.Add("Does not recall all of your memories.");
        if (!profile.AllowRemember) limits.Add("Cannot save new memories.");
        if (!profile.ContributeToLearning) limits.Add("What happens here is not used for learning.");
        var language = string.IsNullOrWhiteSpace(profile.ReplyLanguage) ? "Your usual language" : profile.ReplyLanguage!;
        var learning = profile.ContributeToLearning ? "Counts toward learning" : "Kept out of learning";
        return new ProfileScopeSummary(profile.Name, profile.Version, skills, files, memory, language, learning, limits,
            $"Conversations and tasks already started keep the version they began with (v{profile.Version} now); later changes apply to new ones.");
    }

    public static AssistantProfileSnapshot SnapshotOf(AssistantProfileDraft draft, DateTimeOffset now) => new(
        Guid.Empty, 0, draft.Name, draft.Description, draft.PersonaInstructions, draft.PreferredName, draft.ReplyLanguage,
        draft.IncludeOwnerPersona, draft.RestrictSkills, draft.EnabledSkillIds ?? [], draft.RestrictFiles,
        draft.AllowedCollectionIds ?? [], draft.ModelClass, draft.ChatModel, draft.FastModel, draft.ReasoningEffort,
        draft.MemoryScope, draft.IncludePinnedMemories, draft.ContributeToLearning, draft.AllowPersonaLearning,
        draft.AllowRemember, now);

    /// <summary>What the next profile can reach that the current one cannot. Empty means nothing is widened.</summary>
    public static IReadOnlyList<string> Broadens(AssistantProfileSnapshot current, AssistantProfileSnapshot next)
    {
        var wider = new List<string>();
        if (current.RestrictFiles && (!next.RestrictFiles || next.AllowedCollectionIds.Except(current.AllowedCollectionIds).Any()))
            wider.Add("It can search more of your documents.");
        if (current.RestrictSkills && (!next.RestrictSkills || next.EnabledSkillIds.Except(current.EnabledSkillIds).Any()))
            wider.Add("It can use more of your skills.");
        if (MemoryRank(next) > MemoryRank(current)) wider.Add("It can recall more of your memories.");
        if (!current.AllowRemember && next.AllowRemember) wider.Add("It can save new memories.");
        return wider;
    }

    private static int MemoryRank(AssistantProfileSnapshot snapshot) => snapshot.MemoryScope switch
    {
        MemoryScopes.All => 3,
        MemoryScopes.Profile => 2,
        _ => 1
    };

    /// <summary>
    /// Suggests one of the owner's profiles, or a preset to create, for a task described in the owner's words. It only
    /// explains and compares; the owner decides, and a switch that widens access always needs confirmation.
    /// </summary>
    public static ProfileSuggestion Suggest(string? text, IReadOnlyList<AssistantProfileRecord> owned, Guid? currentId,
        DateTimeOffset now)
    {
        var words = (text ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(words))
            return None("Say what you are about to do and Jarvis can suggest a working style.");
        var best = Presets
            .Select(p => (Preset: p, Hits: p.Keywords.Where(k => words.Contains(k, StringComparison.Ordinal)).Distinct().ToArray()))
            .Where(x => x.Hits.Length > 0)
            .OrderByDescending(x => x.Hits.Length)
            .FirstOrDefault();
        if (best.Preset is null) return None("Nothing here points to a particular working style, so nothing changes.");

        var current = owned.FirstOrDefault(p => p.Id == currentId) ?? owned.FirstOrDefault(p => p.IsDefault);
        var match = owned.FirstOrDefault(p => p.Name.Equals(best.Preset.Name, StringComparison.OrdinalIgnoreCase));
        var reason = $"You mentioned {string.Join(", ", best.Hits.Take(3))}, which fits “{best.Preset.Name}”: {best.Preset.Description}";
        if (match is not null && match.Id == current?.Id)
            return None($"You are already using “{match.Name}”, which fits this.");

        var currentSnapshot = current is null ? null : AssistantProfileSnapshot.From(current, now);
        if (match is not null)
        {
            var next = AssistantProfileSnapshot.From(match, now);
            var change = currentSnapshot is null ? null : ProfileScope.Compare(currentSnapshot, next);
            var broadens = currentSnapshot is null ? [] : Broadens(currentSnapshot, next);
            return new ProfileSuggestion(SuggestionKinds.Existing, match.Id, best.Preset.Id, match.Name, reason, change,
                broadens, change?.RequiresConfirmation == true || broadens.Count > 0,
                "Nothing changes unless you accept. You can also keep your current profile.");
        }

        var draftSnapshot = SnapshotOf(best.Preset.Draft, now);
        var presetChange = currentSnapshot is null ? null : ProfileScope.Compare(currentSnapshot, draftSnapshot);
        var presetBroadens = currentSnapshot is null ? [] : Broadens(currentSnapshot, draftSnapshot);
        return new ProfileSuggestion(SuggestionKinds.Preset, null, best.Preset.Id, best.Preset.Name, reason, presetChange,
            presetBroadens, true,
            best.Preset.NeedsSources
                ? "You do not have this profile yet. Creating it does not switch you to it, and you still choose which documents or skills it may use."
                : "You do not have this profile yet. Creating it does not switch you to it.");
    }

    private static ProfileSuggestion None(string note) =>
        new(SuggestionKinds.None, null, null, null, note, null, [], false, note);

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

public sealed record ProfileGuidanceOperation<T>(T? Value, string? Field = null, string? Message = null, bool NotFound = false)
{
    public bool Succeeded => Field is null && !NotFound;
    public static ProfileGuidanceOperation<T> Ok(T value) => new(value);
    public static ProfileGuidanceOperation<T> Invalid(string field, string message) => new(default, field, message);
    public static ProfileGuidanceOperation<T> Missing() => new(default, null, null, true);
}

public sealed record PresetView(ProfilePreset Preset, Guid? ExistingProfileId, ProfileScopeSummary Scope);

public interface IProfileGuidanceService
{
    Task<IReadOnlyList<PresetView>> PresetsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ProfileGuidanceOperation<AssistantProfileRecord>> CreateFromPresetAsync(Guid ownerId, string presetId,
        CancellationToken cancellationToken);
    Task<ProfileSuggestion> SuggestAsync(Guid ownerId, string? text, Guid? currentProfileId,
        CancellationToken cancellationToken);
    Task<ProfileGuidanceOperation<ProfileScopeSummary>> ScopeAsync(Guid ownerId, Guid profileId,
        CancellationToken cancellationToken);
}

public sealed class ProfileGuidanceService(IAssistantProfileService profiles, TimeProvider? timeProvider = null)
    : IProfileGuidanceService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<PresetView>> PresetsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var owned = await profiles.ListAsync(ownerId, cancellationToken);
        return ProfileGuidance.Presets.Select(p =>
        {
            var existing = owned.FirstOrDefault(o => o.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            var preview = existing ?? new AssistantProfileRecord(Guid.Empty, ownerId, p.Draft.Name, p.Draft.Description, false,
                1, p.Draft.PersonaInstructions, null, null, p.Draft.IncludeOwnerPersona, p.Draft.RestrictSkills, [],
                p.Draft.RestrictFiles, [], null, null, null, null, p.Draft.MemoryScope, p.Draft.IncludePinnedMemories,
                p.Draft.ContributeToLearning, p.Draft.AllowPersonaLearning, p.Draft.AllowRemember, clock.GetUtcNow(), clock.GetUtcNow());
            return new PresetView(p, existing?.Id, ProfileGuidance.Summarise(preview));
        }).ToArray();
    }

    public async Task<ProfileGuidanceOperation<AssistantProfileRecord>> CreateFromPresetAsync(Guid ownerId,
        string presetId, CancellationToken cancellationToken)
    {
        if (ProfileGuidance.FindPreset(presetId) is not { } preset)
            return ProfileGuidanceOperation<AssistantProfileRecord>.Missing();
        var owned = await profiles.ListAsync(ownerId, cancellationToken);
        if (owned.Any(o => o.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase)))
            return ProfileGuidanceOperation<AssistantProfileRecord>.Invalid("preset", $"You already have a profile called “{preset.Name}”.");
        try
        {
            // Never the default: creating a style must not change how every conversation starts.
            return ProfileGuidanceOperation<AssistantProfileRecord>.Ok(
                await profiles.CreateAsync(ownerId, preset.Draft with { IsDefault = false }, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return ProfileGuidanceOperation<AssistantProfileRecord>.Invalid("preset", exception.Message);
        }
    }

    public async Task<ProfileSuggestion> SuggestAsync(Guid ownerId, string? text, Guid? currentProfileId,
        CancellationToken cancellationToken)
    {
        var clean = text is { Length: > ProfileGuidance.MaxTextLength } ? text[..ProfileGuidance.MaxTextLength] : text;
        return ProfileGuidance.Suggest(clean, await profiles.ListAsync(ownerId, cancellationToken), currentProfileId,
            clock.GetUtcNow());
    }

    public async Task<ProfileGuidanceOperation<ProfileScopeSummary>> ScopeAsync(Guid ownerId, Guid profileId,
        CancellationToken cancellationToken) =>
        await profiles.GetAsync(profileId, ownerId, cancellationToken) is { } profile
            ? ProfileGuidanceOperation<ProfileScopeSummary>.Ok(ProfileGuidance.Summarise(profile))
            : ProfileGuidanceOperation<ProfileScopeSummary>.Missing();
}
