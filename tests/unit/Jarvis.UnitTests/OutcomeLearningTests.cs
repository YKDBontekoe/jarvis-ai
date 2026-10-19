using Jarvis.Application.Outcomes;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class OutcomeLearningTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fc11");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fc12");
    private static readonly Guid Restricted = Guid.Parse("01996b8c-6000-7000-8000-00000000fc21");
    private static readonly Guid OpenProfile = Guid.Parse("01996b8c-6000-7000-8000-00000000fc22");
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Time = Start;
        public override DateTimeOffset GetUtcNow() => Time;
    }

    private static AssistantProfileRecord Profile(Guid id, bool learns) =>
        new(id, Owner, "p", null, false, 1, null, null, null, true, false, [], false, [], null, null, null, null, "all", true,
            learns, true, true, Start, Start);

    private static (OutcomeLearningService Service, Clock Time, InMemorySettingsStore Store) Create()
    {
        var store = new InMemorySettingsStore();
        var time = new Clock();
        var profiles = Fake<IAssistantProfileService>.Create(("GetAsync", args =>
            (Guid)args[0]! == Restricted ? Profile(Restricted, false) : (Guid)args[0]! == OpenProfile ? Profile(OpenProfile, true) : null));
        return (new OutcomeLearningService(store, profiles, time), time, store);
    }

    private static async Task Say(OutcomeLearningService service, int times, string kind = "project_update",
        string signal = "revised", string? aspect = "too_long", Guid? profile = null, Guid? owner = null)
    {
        for (var i = 0; i < times; i++)
            Assert.True((await service.RecordAsync(owner ?? Owner, kind, null, signal, aspect, null, profile, default)).Value!.Recorded);
    }

    [Fact]
    public async Task Repeated_too_long_feedback_yields_a_reviewable_style_change_with_a_labelled_example()
    {
        var (service, _, _) = Create();
        await Say(service, 2);
        Assert.Empty(await service.ChangesAsync(Owner, default));

        await Say(service, 1);
        var change = Assert.Single(await service.ChangesAsync(Owner, default)).Change;

        Assert.Equal("pending", change.Status);
        Assert.Equal("Keep project update short", change.Title);
        Assert.Contains("3 times", change.Reason);
        Assert.True(change.Synthetic);
        Assert.NotEqual(change.Before, change.After);
        Assert.Null(await service.StyleNoteAsync(Owner, null, default));
    }

    [Fact]
    public async Task Applying_changes_the_style_note_and_undo_removes_it()
    {
        var (service, _, _) = Create();
        await Say(service, 3);
        var id = (await service.ChangesAsync(Owner, default)).Single().Change.Id;

        var applied = await service.ApplyAsync(Owner, id, default);
        Assert.Equal("applied", applied.Value!.Change.Status);
        Assert.Contains("project update: keep it short", await service.StyleNoteAsync(Owner, null, default));

        Assert.Equal("status", (await service.ApplyAsync(Owner, id, default)).Field);
        var undone = await service.UndoAsync(Owner, id, default);
        Assert.Equal("undone", undone.Value!.Change.Status);
        Assert.Null(await service.StyleNoteAsync(Owner, null, default));
    }

    [Fact]
    public async Task A_rejected_or_undone_change_is_never_offered_again()
    {
        var (service, _, _) = Create();
        await Say(service, 3);
        var id = (await service.ChangesAsync(Owner, default)).Single().Change.Id;
        await service.RejectAsync(Owner, id, default);

        await Say(service, 5);

        Assert.Empty(await service.ChangesAsync(Owner, default));
        Assert.Null(await service.StyleNoteAsync(Owner, null, default));
        Assert.Equal("status", (await service.ApplyAsync(Owner, id, default)).Field);
    }

    [Fact]
    public async Task Feedback_is_not_pooled_across_profiles_or_owners()
    {
        var (service, _, _) = Create();
        await Say(service, 2, profile: OpenProfile);
        await Say(service, 2);
        await Say(service, 5, owner: Other);

        Assert.Empty(await service.ChangesAsync(Owner, default));
        Assert.Single(await service.ChangesAsync(Other, default));

        await Say(service, 1, profile: OpenProfile);
        var change = Assert.Single(await service.ChangesAsync(Owner, default)).Change;
        Assert.Equal(OpenProfile, change.ProfileId);
        await service.ApplyAsync(Owner, change.Id, default);
        Assert.NotNull(await service.StyleNoteAsync(Owner, OpenProfile, default));
        Assert.Null(await service.StyleNoteAsync(Owner, null, default));
        Assert.Null(await service.StyleNoteAsync(Owner, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_profile_kept_out_of_learning_stores_nothing()
    {
        var (service, _, store) = Create();

        var result = await service.RecordAsync(Owner, "project_update", null, "revised", "too_long", null, Restricted, default);

        Assert.False(result.Value!.Recorded);
        Assert.Contains("kept out of learning", result.Value.Message);
        Assert.Null(await store.GetAsync<OutcomeState>(Owner, SettingsSections.Outcomes, default));
        Assert.Equal("profileId", (await service.RecordAsync(Owner, "trip", null, "accepted", null, null, Guid.NewGuid(), default)).Field);
    }

    [Fact]
    public async Task Opting_out_of_proposals_or_signals_is_respected()
    {
        var (service, _, store) = Create();
        await Say(service, 3);
        await store.SaveAsync(Owner, SettingsSections.Learning, LearningSettings.Default with { ProposeImprovements = false }, default);
        Assert.Empty(await service.ChangesAsync(Owner, default));

        await store.SaveAsync(Owner, SettingsSections.Learning, LearningSettings.Default with { CaptureSignals = false }, default);
        var off = await service.RecordAsync(Owner, "trip", null, "accepted", null, null, null, default);
        Assert.False(off.Value!.Recorded);
    }

    [Fact]
    public async Task Repeated_dismissals_can_quiet_a_volunteered_kind_without_touching_permissions()
    {
        var (service, _, _) = Create();
        await Say(service, 3, kind: "opportunity", signal: "dismissed", aspect: null);

        var change = (await service.ChangesAsync(Owner, default)).Single().Change;
        Assert.Equal("quiet", change.Kind);
        await service.ApplyAsync(Owner, change.Id, default);

        Assert.Contains("Do not volunteer opportunity suggestions unless the owner asks", await service.StyleNoteAsync(Owner, null, default));
        // A change carries only a kind, a value and prose: nothing in it names a tool, a permission or a scope.
        Assert.DoesNotContain(typeof(BehaviorChange).GetProperties(),
            p => p.Name.Contains("Tool") || p.Name.Contains("Permission") || p.Name.Contains("Scope"));
    }

    [Fact]
    public async Task Effect_is_too_early_at_first_then_reports_a_small_sample_honestly()
    {
        var (service, time, _) = Create();
        await Say(service, 3);
        var id = (await service.ChangesAsync(Owner, default)).Single().Change.Id;
        time.Time = Start.AddDays(1);
        await service.ApplyAsync(Owner, id, default);

        Assert.Contains("Too early", (await service.ChangesAsync(Owner, default)).Single().Effect!.Statement);

        time.Time = Start.AddDays(10);
        var effect = (await service.ChangesAsync(Owner, default)).Single().Effect!;
        Assert.Equal(3, effect.Before);
        Assert.Equal(0, effect.After);
        Assert.Contains("small sample", effect.Statement);
    }

    [Fact]
    public async Task The_weekly_card_shows_changes_not_counts_and_stays_quiet_when_there_is_nothing()
    {
        var (service, time, _) = Create();
        Assert.Contains("Nothing new this week", (await service.LearnedAsync(Owner, default)).Summary);

        await Say(service, 3);
        var waiting = await service.LearnedAsync(Owner, default);
        Assert.Single(waiting.Waiting);
        Assert.Contains("Nothing changes until you say so", waiting.Summary);

        await service.ApplyAsync(Owner, waiting.Waiting[0].Change.Id, default);
        Assert.Single((await service.LearnedAsync(Owner, default)).Applied);
        time.Time = Start.AddDays(20);
        Assert.Empty((await service.LearnedAsync(Owner, default)).Applied);
    }

    [Fact]
    public async Task Invalid_feedback_is_refused_and_absence_is_never_acceptance()
    {
        var (service, _, store) = Create();

        Assert.Equal("artifactKind", (await service.RecordAsync(Owner, "nope", null, "accepted", null, null, null, default)).Field);
        Assert.Equal("signal", (await service.RecordAsync(Owner, "trip", null, "silence", null, null, null, default)).Field);
        Assert.Equal("aspect", (await service.RecordAsync(Owner, "trip", null, "revised", "weird", null, null, default)).Field);
        Assert.Equal("note", (await service.RecordAsync(Owner, "trip", null, "revised", null, new string('x', 201), null, default)).Field);
        Assert.Null(await store.GetAsync<OutcomeState>(Owner, SettingsSections.Outcomes, default));
        Assert.Empty(await service.ChangesAsync(Owner, default));
    }

    [Fact]
    public async Task Suggestion_use_counts_only_answers_and_says_when_there_are_too_few()
    {
        var (service, _, _) = Create();
        Assert.Empty((await service.LearnedAsync(Owner, default)).Suggestions);

        await Say(service, 2, kind: "opportunity", signal: "accepted", aspect: null);
        var few = (await service.LearnedAsync(Owner, default)).Suggestions.Single();
        Assert.Equal(2, few.Rated);
        Assert.Contains("too few", few.Statement);

        await Say(service, 1, kind: "opportunity", signal: "not_useful", aspect: null);
        await Say(service, 1, kind: "opportunity", signal: "revised", aspect: "too_long");
        var enough = (await service.LearnedAsync(Owner, default)).Suggestions.Single();
        Assert.Equal(2, enough.Used);
        Assert.Equal(1, enough.NotUsed);
        Assert.Equal(3, enough.Rated);
        Assert.Contains("You used 2 of the 3 you answered", enough.Statement);
        Assert.Contains("not counted", enough.Statement);
    }

    [Fact]
    public async Task Old_answers_age_out_of_the_suggestion_summary()
    {
        var (service, time, _) = Create();
        await Say(service, 3, kind: "suggestion", signal: "accepted", aspect: null);
        time.Time = Start.AddDays(45);

        Assert.Empty((await service.LearnedAsync(Owner, default)).Suggestions);
    }
}
