using Jarvis.Agents.Persona;
using Jarvis.Application.Persona;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PersonaTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000cafe");

    [Fact]
    public async Task Learning_the_same_statement_reinforces_instead_of_duplicating()
    {
        var persona = new PersonaService(new InMemorySettingsStore());

        await persona.LearnAsync(Owner, new PersonaObservation("format", "Keep answers short.", 0.7f), default);
        var reinforced = await persona.LearnAsync(Owner,
            new PersonaObservation("format", "keep answers SHORT", 0.6f), default);

        var trait = Assert.Single((await persona.GetAsync(Owner, default)).TraitList);
        Assert.Equal(reinforced.Id, trait.Id);
        Assert.Equal(2, trait.Evidence);
        Assert.True(trait.Confidence > 0.7f);
    }

    [Fact]
    public async Task Learned_observations_replace_learned_traits_but_never_pinned_user_traits()
    {
        var persona = new PersonaService(new InMemorySettingsStore());
        var learned = await persona.LearnAsync(Owner, new PersonaObservation("language", "Reply in English.", 0.8f), default);
        var pinned = await persona.AddUserTraitAsync(Owner, "tone", "Be direct and skip small talk.", default);

        await persona.LearnAsync(Owner, new PersonaObservation("language", "Reply in Dutch.", 0.9f, learned.Id), default);
        await persona.LearnAsync(Owner, new PersonaObservation("tone", "Be chatty and warm.", 0.9f, pinned.Id), default);

        var statements = (await persona.GetAsync(Owner, default)).TraitList.Select(trait => trait.Statement).ToArray();
        Assert.DoesNotContain("Reply in English.", statements);
        Assert.Contains("Reply in Dutch.", statements);
        Assert.Contains("Be direct and skip small talk.", statements);
    }

    [Fact]
    public async Task Weak_or_invalid_observations_are_rejected()
    {
        var persona = new PersonaService(new InMemorySettingsStore());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            persona.LearnAsync(Owner, new PersonaObservation("tone", "Maybe likes emoji.", 0.3f), default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            persona.LearnAsync(Owner, new PersonaObservation("tone", "ok", 0.9f), default));
        Assert.Empty((await persona.GetAsync(Owner, default)).TraitList);
    }

    [Fact]
    public async Task Persona_stays_bounded_by_evicting_the_weakest_unpinned_trait()
    {
        var persona = new PersonaService(new InMemorySettingsStore());
        await persona.AddUserTraitAsync(Owner, "tone", "Never use exclamation marks.", default);
        for (var index = 0; index < PersonaService.MaxTraits + 5; index++)
            await persona.LearnAsync(Owner,
                new PersonaObservation("other", $"Learned preference number {index}.", 0.6f + index / 1000f), default);

        var traits = (await persona.GetAsync(Owner, default)).TraitList;
        Assert.Equal(PersonaService.MaxTraits, traits.Count);
        Assert.Contains(traits, trait => trait.Statement == "Never use exclamation marks.");
        Assert.DoesNotContain(traits, trait => trait.Statement == "Learned preference number 0.");
    }

    [Fact]
    public void Context_renders_name_language_traits_and_custom_instructions()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new PersonaProfile("I run a bakery; mornings are busy.", "Sam", "Dutch",
        [
            new PersonaTrait(Guid.NewGuid(), "format", "Use bullet lists for options.", 0.8f, 3, "learned", false, now, now),
            new PersonaTrait(Guid.NewGuid(), "tone", "Be direct.", 1f, 1, "user", true, now, now)
        ]);

        var rendered = PersonaContextProvider.Render(profile)!;

        Assert.StartsWith(PersonaContextProvider.Prefix, rendered);
        Assert.Contains("Address the user as Sam.", rendered);
        Assert.Contains("Reply in Dutch", rendered);
        Assert.True(rendered.IndexOf("Be direct.", StringComparison.Ordinal) <
                    rendered.IndexOf("Use bullet lists", StringComparison.Ordinal), "Pinned traits come first.");
        Assert.Contains("I run a bakery", rendered);
        Assert.Null(PersonaContextProvider.Render(PersonaProfile.Empty));
    }
}
