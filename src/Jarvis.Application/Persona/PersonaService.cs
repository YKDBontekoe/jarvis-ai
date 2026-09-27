using Jarvis.Application.Settings;

namespace Jarvis.Application.Persona;

/// <summary>Owns the learned persona: validation, de-duplication, reinforcement, and bounded size.</summary>
public sealed class PersonaService(IOwnerSettingsStore settings, TimeProvider? clock = null)
{
    public const int MaxTraits = 40;
    public const int MaxStatementLength = 280;
    public const int MaxInstructionsLength = 4_000;
    public const float MinimumLearnedConfidence = 0.55f;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PersonaProfile> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<PersonaProfile>(ownerId, SettingsSections.Persona, cancellationToken)
        ?? PersonaProfile.Empty;

    public async Task<PersonaProfile> SaveProfileAsync(Guid ownerId, string? customInstructions, string? preferredName,
        string? replyLanguage, CancellationToken cancellationToken)
    {
        var instructions = customInstructions?.Trim();
        if (instructions?.Length > MaxInstructionsLength)
            throw new ArgumentException("Keep custom instructions under 4,000 characters.", nameof(customInstructions));
        var name = preferredName?.Trim();
        if (name?.Length > 60) throw new ArgumentException("Keep the preferred name under 60 characters.", nameof(preferredName));
        var language = replyLanguage?.Trim();
        if (language?.Length > 40) throw new ArgumentException("Keep the reply language under 40 characters.", nameof(replyLanguage));
        var current = await GetAsync(ownerId, cancellationToken);
        var updated = current with
        {
            CustomInstructions = string.IsNullOrEmpty(instructions) ? null : instructions,
            PreferredName = string.IsNullOrEmpty(name) ? null : name,
            ReplyLanguage = string.IsNullOrEmpty(language) ? null : language
        };
        await SaveAsync(ownerId, updated, cancellationToken);
        return updated;
    }

    /// <summary>Adds a user-stated trait, which is pinned and never decays.</summary>
    public Task<PersonaTrait> AddUserTraitAsync(Guid ownerId, string category, string statement,
        CancellationToken cancellationToken) =>
        UpsertAsync(ownerId, new PersonaObservation(category, statement, 1f), "user", pinned: true, cancellationToken);

    /// <summary>Records a learned observation: reinforces a matching trait or adds/replaces one.</summary>
    public Task<PersonaTrait> LearnAsync(Guid ownerId, PersonaObservation observation,
        CancellationToken cancellationToken) =>
        UpsertAsync(ownerId, observation, "learned", pinned: false, cancellationToken);

    public async Task<PersonaTrait?> UpdateTraitAsync(Guid ownerId, Guid traitId, string? statement, bool? pinned,
        CancellationToken cancellationToken)
    {
        var profile = await GetAsync(ownerId, cancellationToken);
        var traits = profile.TraitList.ToList();
        var index = traits.FindIndex(trait => trait.Id == traitId);
        if (index < 0) return null;
        var trait = traits[index];
        if (statement is not null) trait = trait with { Statement = NormalizeStatement(statement), Source = "user" };
        if (pinned is not null) trait = trait with { Pinned = pinned.Value };
        traits[index] = trait with { UpdatedAt = _clock.GetUtcNow() };
        await SaveAsync(ownerId, profile with { Traits = traits }, cancellationToken);
        return traits[index];
    }

    public async Task<bool> RemoveTraitAsync(Guid ownerId, Guid traitId, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(ownerId, cancellationToken);
        var traits = profile.TraitList.Where(trait => trait.Id != traitId).ToList();
        if (traits.Count == profile.TraitList.Count) return false;
        await SaveAsync(ownerId, profile with { Traits = traits }, cancellationToken);
        return true;
    }

    public async Task MarkReflectedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(ownerId, cancellationToken);
        await SaveAsync(ownerId, profile with { LastReflectedAt = _clock.GetUtcNow() }, cancellationToken);
    }

    private async Task<PersonaTrait> UpsertAsync(Guid ownerId, PersonaObservation observation, string source,
        bool pinned, CancellationToken cancellationToken)
    {
        var statement = NormalizeStatement(observation.Statement);
        var category = PersonaCategories.Normalize(observation.Category);
        var confidence = Math.Clamp(observation.Confidence, 0f, 1f);
        if (source == "learned" && confidence < MinimumLearnedConfidence)
            throw new ArgumentException("The observation is not confident enough to learn.", nameof(observation));
        var now = _clock.GetUtcNow();
        var profile = await GetAsync(ownerId, cancellationToken);
        var traits = profile.TraitList.ToList();

        var existing = traits.FindIndex(trait => SameStatement(trait.Statement, statement));
        if (existing >= 0)
        {
            var match = traits[existing];
            traits[existing] = match with
            {
                Evidence = match.Evidence + 1,
                Confidence = Math.Min(1f, Math.Max(match.Confidence, confidence) + 0.05f),
                Pinned = match.Pinned || pinned,
                Source = match.Source == "user" ? "user" : source,
                UpdatedAt = now
            };
            await SaveAsync(ownerId, profile with { Traits = traits }, cancellationToken);
            return traits[existing];
        }

        if (observation.ReplacesTraitId is { } replacedId)
        {
            var replaced = traits.FindIndex(trait => trait.Id == replacedId);
            if (replaced >= 0 && (source == "user" || !traits[replaced].Pinned)) traits.RemoveAt(replaced);
        }

        var trait = new PersonaTrait(Guid.CreateVersion7(), category, statement, confidence, 1, source, pinned, now, now);
        traits.Add(trait);
        if (traits.Count > MaxTraits)
        {
            var evict = traits.Where(item => !item.Pinned && item.Id != trait.Id)
                .OrderBy(item => item.Confidence * Math.Log(1 + item.Evidence)).ThenBy(item => item.UpdatedAt)
                .FirstOrDefault()
                ?? throw new ArgumentException("The persona is full of pinned traits; remove one first.");
            traits.Remove(evict);
        }
        await SaveAsync(ownerId, profile with { Traits = traits }, cancellationToken);
        return trait;
    }

    private Task SaveAsync(Guid ownerId, PersonaProfile profile, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, SettingsSections.Persona, profile, cancellationToken);

    private static string NormalizeStatement(string statement)
    {
        var value = string.Join(' ', (statement ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        if (value.Length is < 6 or > MaxStatementLength)
            throw new ArgumentException("Describe the preference in 6 to 280 characters.", nameof(statement));
        return value;
    }

    private static bool SameStatement(string left, string right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);

    private static string Canonical(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
