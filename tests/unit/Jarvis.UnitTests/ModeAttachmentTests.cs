using Jarvis.Application.Modes;
using Jarvis.Application.Profiles;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ModeAttachmentTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fe61");
    private static readonly Guid DefaultProfile = Guid.Parse("01996b8c-6000-7000-8000-00000000fe71");
    private static readonly Guid Research = Guid.Parse("01996b8c-6000-7000-8000-00000000fe72");
    private static readonly Guid Wide = Guid.Parse("01996b8c-6000-7000-8000-00000000fe73");
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Monday;
    }

    private static AssistantProfileRecord Profile(Guid id, string name, bool isDefault, bool restricted) =>
        new(id, Owner, name, null, isDefault, 1, null, null, null, true, false, [], restricted, restricted ? [Guid.NewGuid()] : [],
            null, null, null, null, restricted ? "pinned" : "all", true, true, true, !restricted, Monday, Monday);

    private static (ModeAttachmentService Service, ModeService Modes, InMemorySettingsStore Store, List<AssistantProfileRecord> Profiles) Create()
    {
        var store = new InMemorySettingsStore();
        var owned = new List<AssistantProfileRecord>
        {
            Profile(DefaultProfile, "Default", true, false),
            Profile(Research, "Research analyst", false, true),
            Profile(Wide, "Planning partner", false, false)
        };
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));
        var modes = new ModeService(store, briefings, null, new Clock());
        var profiles = Fake<IAssistantProfileService>.Create(("ListAsync", _ => owned.ToArray()));
        return (new ModeAttachmentService(store, modes, profiles, new Clock()), modes, store, owned);
    }

    [Fact]
    public async Task Nothing_is_suggested_until_the_owner_attaches_something()
    {
        var (service, _, _, _) = Create();

        var suggestion = await service.SuggestAsync(Owner, null, default);

        Assert.Null(suggestion.Profile);
        Assert.Null(suggestion.Routine);
        Assert.Empty(suggestion.VisualOnly);
        Assert.Empty(await service.ListAsync(Owner, null, default));
    }

    [Fact]
    public async Task The_suggestion_follows_the_current_mode_and_separates_looking_from_doing()
    {
        var (service, modes, _, _) = Create();
        await service.SetAsync(Owner, "focus", Research, "focus", default);
        await modes.SetModeAsync(Owner, "focus", 45, default);

        var suggestion = await service.SuggestAsync(Owner, DefaultProfile, default);

        Assert.Equal("focus", suggestion.Mode);
        Assert.Equal("Research analyst", suggestion.Profile!.Name);
        Assert.Equal("Start focus", suggestion.Routine!.Title);
        Assert.Contains(suggestion.VisualOnly, x => x.Contains("suggestion"));
        Assert.Contains(suggestion.NeedsYou, x => x.Contains("Nothing switches profile by itself"));
        Assert.Contains(suggestion.NeedsYou, x => x.Contains("security controls always need your approval"));

        await modes.SetModeAsync(Owner, "meeting", 30, default);
        Assert.Null((await service.SuggestAsync(Owner, DefaultProfile, default)).Profile);
    }

    [Fact]
    public async Task A_profile_that_would_widen_access_is_called_out_and_a_narrowing_one_is_not()
    {
        var (service, modes, _, _) = Create();
        await service.SetAsync(Owner, "focus", Wide, null, default);
        await modes.SetModeAsync(Owner, "focus", 45, default);

        var widening = (await service.SuggestAsync(Owner, Research, default)).Profile!;
        Assert.Contains("It can recall more of your memories.", widening.Broadens);
        Assert.True(widening.ScopeChange!.RequiresConfirmation);

        await service.SetAsync(Owner, "focus", Research, null, default);
        var narrowing = (await service.SuggestAsync(Owner, DefaultProfile, default)).Profile!;
        Assert.Empty(narrowing.Broadens);
        Assert.NotNull(narrowing.ScopeChange);
    }

    [Fact]
    public async Task Attaching_the_profile_already_in_use_shows_no_change()
    {
        var (service, modes, _, _) = Create();
        await service.SetAsync(Owner, "focus", Research, null, default);
        await modes.SetModeAsync(Owner, "focus", 45, default);

        var profile = (await service.SuggestAsync(Owner, Research, default)).Profile!;

        Assert.Null(profile.ScopeChange);
        Assert.Empty(profile.Broadens);
    }

    [Fact]
    public async Task Input_is_validated_and_clearing_removes_the_attachment()
    {
        var (service, _, _, _) = Create();

        Assert.Equal("mode", (await service.SetAsync(Owner, "nope", null, null, default)).Field);
        Assert.Equal("routineId", (await service.SetAsync(Owner, "focus", null, "bedtime", default)).Field);
        Assert.Equal("profileId", (await service.SetAsync(Owner, "focus", Guid.NewGuid(), null, default)).Field);

        await service.SetAsync(Owner, "focus", Research, "focus", default);
        Assert.Single(await service.ListAsync(Owner, null, default));
        await service.SetAsync(Owner, "focus", null, null, default);
        Assert.Empty(await service.ListAsync(Owner, null, default));
    }

    [Fact]
    public async Task A_deleted_profile_is_reported_not_silently_replaced()
    {
        var (service, modes, _, owned) = Create();
        await service.SetAsync(Owner, "focus", Research, null, default);
        await modes.SetModeAsync(Owner, "focus", 45, default);
        owned.RemoveAll(p => p.Id == Research);

        var suggestion = await service.SuggestAsync(Owner, DefaultProfile, default);

        Assert.Null(suggestion.Profile);
        Assert.Contains("was deleted", suggestion.Problem);
        Assert.Contains("was deleted", (await service.ListAsync(Owner, null, default)).Single().Problem);
    }
}
