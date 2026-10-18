using Jarvis.Application.Profiles;
using Jarvis.Domain.Profiles;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProfileGuidanceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fb11");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static AssistantProfileRecord Profile(string name, bool isDefault = false, bool restrictFiles = false,
        string memory = "all", bool remember = true, int collections = 0, int version = 1) =>
        new(Guid.NewGuid(), Owner, name, null, isDefault, version, null, null, null, true, false, [], restrictFiles,
            Enumerable.Range(0, collections).Select(_ => Guid.NewGuid()).ToArray(), null, null, null, null, memory, true,
            true, true, remember, Now, Now);

    [Fact]
    public void Presets_are_five_working_styles_with_honest_scope()
    {
        Assert.Equal(["planning", "research", "writing", "tutor", "operator"], ProfileGuidance.Presets.Select(p => p.Id));
        var research = ProfileGuidance.FindPreset("research")!;
        Assert.True(research.NeedsSources);
        Assert.False(research.Draft.AllowRemember);
        Assert.Null(research.Draft.IsDefault);
        Assert.All(ProfileGuidance.Presets, p => Assert.False(p.Draft.RestrictFiles || p.Draft.RestrictSkills));
    }

    [Fact]
    public void A_suggestion_names_the_words_it_matched_and_never_picks_for_nothing()
    {
        var owned = new[] { Profile("Default", isDefault: true) };

        var edit = ProfileGuidance.Suggest("Please rewrite this email draft", owned, null, Now);
        Assert.Equal("preset", edit.Kind);
        Assert.Equal("writing", edit.PresetId);
        Assert.Contains("rewrite", edit.Reason);
        Assert.True(edit.RequiresConfirmation);

        Assert.Equal("none", ProfileGuidance.Suggest("what is the weather", owned, null, Now).Kind);
        Assert.Equal("none", ProfileGuidance.Suggest("   ", owned, null, Now).Kind);
    }

    [Fact]
    public void An_existing_profile_is_preferred_and_the_current_one_is_left_alone()
    {
        var def = Profile("Default", isDefault: true);
        var tutor = Profile("Tutor", memory: "profile");
        var owned = new[] { def, tutor };

        var suggestion = ProfileGuidance.Suggest("explain this exam topic", owned, def.Id, Now);
        Assert.Equal("existing", suggestion.Kind);
        Assert.Equal(tutor.Id, suggestion.ProfileId);
        Assert.NotNull(suggestion.ScopeChange);
        Assert.True(suggestion.ScopeChange!.MemoryChanged);

        var already = ProfileGuidance.Suggest("explain this exam topic", owned, tutor.Id, Now);
        Assert.Equal("none", already.Kind);
        Assert.Contains("already using", already.Note);
    }

    [Fact]
    public void A_switch_that_widens_access_is_called_out_and_needs_confirmation()
    {
        var restricted = Profile("Research analyst", restrictFiles: true, memory: "pinned", remember: false, collections: 1);
        var wide = Profile("Writing editor");
        var suggestion = ProfileGuidance.Suggest("edit my essay", [restricted, wide], restricted.Id, Now);

        Assert.Equal(wide.Id, suggestion.ProfileId);
        Assert.Contains("It can search more of your documents.", suggestion.Broadens);
        Assert.Contains("It can recall more of your memories.", suggestion.Broadens);
        Assert.Contains("It can save new memories.", suggestion.Broadens);
        Assert.True(suggestion.RequiresConfirmation);

        var narrowing = ProfileGuidance.Suggest("research these sources", [wide, restricted], wide.Id, Now);
        Assert.Empty(narrowing.Broadens);
    }

    [Fact]
    public void The_scope_summary_lists_limits_and_keeps_old_conversations_understandable()
    {
        var summary = ProfileGuidance.Summarise(Profile("Research analyst", restrictFiles: true, memory: "pinned",
            remember: false, collections: 2, version: 3));

        Assert.Equal("2 document collections", summary.Files);
        Assert.Equal("Only pinned memories", summary.Memory);
        Assert.Contains("Cannot save new memories.", summary.Limits);
        Assert.Contains("Can only search the document collections chosen for this profile.", summary.Limits);
        Assert.Contains("keep the version they began with (v3 now)", summary.SnapshotNote);

        var open = ProfileGuidance.Summarise(Profile("Default", isDefault: true));
        Assert.Empty(open.Limits);
        Assert.Equal("All your documents", open.Files);
    }

    [Fact]
    public async Task Creating_a_preset_never_makes_it_the_default_and_never_duplicates()
    {
        var created = new List<AssistantProfileDraft>();
        var existing = new List<AssistantProfileRecord> { Profile("Default", isDefault: true) };
        var profiles = Fake<IAssistantProfileService>.Create(
            ("ListAsync", _ => existing.ToArray()),
            ("CreateAsync", args =>
            {
                created.Add((AssistantProfileDraft)args[1]!);
                var record = Profile(created[^1].Name);
                existing.Add(record);
                return record;
            }));
        var service = new ProfileGuidanceService(profiles);

        var first = await service.CreateFromPresetAsync(Owner, "tutor", default);
        var again = await service.CreateFromPresetAsync(Owner, "tutor", default);

        Assert.True(first.Succeeded);
        Assert.False(created.Single().IsDefault);
        Assert.Equal("preset", again.Field);
        Assert.True((await service.CreateFromPresetAsync(Owner, "nope", default)).NotFound);
        var views = await service.PresetsAsync(Owner, default);
        Assert.NotNull(views.Single(v => v.Preset.Id == "tutor").ExistingProfileId);
        Assert.Null(views.Single(v => v.Preset.Id == "writing").ExistingProfileId);
    }
}
