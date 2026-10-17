using Jarvis.Agents.People;
using Jarvis.Application.Conversations;
using Jarvis.Application.Inbox;
using Jarvis.Application.People;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.People;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PersonFollowThroughTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fa01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fa02");
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset Noon = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static Person Rosa(Guid? owner = null, int? every = 7, DateTimeOffset? contacted = null,
        int? month = null, int? day = null) =>
        new(Guid.NewGuid(), owner ?? Owner, "Rosa de Vries", "friend", month, day, null, null, every, contacted, null,
            null, null, Noon.AddDays(-60), Noon.AddDays(-60));

    private static Commitment Promise(string who, string what, DateOnly? due = null, string direction = "i_owe",
        bool suggested = false, string status = CommitmentStatuses.Open, Guid? owner = null) =>
        new(Guid.NewGuid(), owner ?? Owner, direction, who, what, due, status, suggested, CommitmentSources.Chat,
            null, null, null, Noon.AddDays(-5), Noon.AddDays(-5));

    private static PersonFollowThrough Build(Person person, IEnumerable<Commitment>? promises = null,
        PersonNudgePref? pref = null) =>
        PersonFollowThroughBuilder.Build(person, Today, TimeZoneInfo.Utc, promises ?? [], pref ?? new PersonNudgePref());

    [Fact]
    public void A_recent_contact_satisfies_the_rhythm_and_says_until_when()
    {
        var view = Build(Rosa(contacted: Noon.AddDays(-2)));

        Assert.Equal("ok", view.Cadence.Status);
        Assert.Contains("covered until 22 Oct", view.Cadence.Reason);
        Assert.Empty(view.Reasons);
    }

    [Fact]
    public void An_overdue_rhythm_is_due_and_explains_how_long_it_has_been()
    {
        var view = Build(Rosa(contacted: Noon.AddDays(-12)));

        Assert.Equal("due", view.Cadence.Status);
        Assert.Contains("12 days ago", view.Cadence.Reason);
        Assert.Contains("every week", view.Cadence.Reason);
        Assert.Single(view.Reasons);
    }

    [Fact]
    public void No_rhythm_and_no_contact_says_there_is_nothing_to_base_a_nudge_on()
    {
        var view = Build(Rosa(every: null));

        Assert.False(view.HasData);
        Assert.Equal("none", view.Cadence.Status);
        Assert.Contains("nothing to base a nudge on", view.Cadence.Reason);
    }

    [Fact]
    public void A_birthday_within_a_week_is_a_reason_and_a_distant_one_is_not()
    {
        var soon = Build(Rosa(every: null, month: 10, day: 20));
        var far = Build(Rosa(every: null, month: 3, day: 2));

        Assert.Equal(3, soon.Birthday!.DaysUntil);
        Assert.Contains("birthday is in 3 days", Assert.Single(soon.Reasons));
        Assert.True(far.Birthday!.DaysUntil > 7);
        Assert.Empty(far.Reasons);
    }

    [Fact]
    public void Only_open_accepted_promises_with_the_exact_name_count_and_late_ones_are_reasons()
    {
        var person = Rosa(every: null);
        var view = Build(person,
        [
            Promise("rosa de vries", "Send the deck", Today.AddDays(-4)),
            Promise("Rosa", "Nickname only"),
            Promise("Rosa de Vries", "Suggested", suggested: true),
            Promise("Rosa de Vries", "Done", status: CommitmentStatuses.Done),
            Promise("Rosa de Vries", "Someone else's", owner: Other),
            Promise("Rosa de Vries", "Borrowed book", Today.AddDays(3), "owed_to_me")
        ]);

        Assert.Equal(["Send the deck", "Borrowed book"], view.Promises.Select(item => item.Description));
        Assert.Equal(4, view.Promises[0].DaysOverdue);
        Assert.StartsWith("commitment:", view.Promises[0].Ref);
        Assert.Contains("You owe Rosa de Vries: Send the deck (4 days late)", Assert.Single(view.Reasons));
    }

    [Theory]
    [InlineData(NudgeModes.On, null, false)]
    [InlineData(NudgeModes.Muted, null, true)]
    [InlineData(NudgeModes.Snoozed, "2026-10-18", true)]
    [InlineData(NudgeModes.Snoozed, "2026-10-17", false)]
    [InlineData(NudgeModes.Snoozed, null, false)]
    public void Snoozing_is_quiet_only_until_its_day(string mode, string? until, bool quiet)
    {
        var pref = new PersonNudgePref(mode, until is null ? null : DateOnly.Parse(until));

        Assert.Equal(quiet, Build(Rosa(), pref: pref).Nudge.Quiet);
    }

    private sealed record Setup(PersonFollowThroughService Service, Person Person, InMemorySettingsStore Settings);

    private static Setup Create(IEnumerable<Commitment>? promises = null)
    {
        var person = Rosa();
        var settings = new InMemorySettingsStore();
        var people = Fake<IPeopleRepository>.Create(("GetAsync", args =>
            (Guid)args[0]! == person.Id && (Guid)args[1]! == Owner ? person : null));
        var list = promises?.ToArray() ?? [];
        var commitments = Fake<ICommitmentService>.Create(("ListAsync", _ => (IReadOnlyList<Commitment>)list));
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));
        return new Setup(new PersonFollowThroughService(people, commitments, settings, briefings,
            new FixedClock(Noon)), person, settings);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Snoozing_a_person_records_the_day_and_turning_it_on_forgets_it()
    {
        var setup = Create();

        var snoozed = await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.Snoozed, 14, default);
        Assert.Equal(Today.AddDays(14), snoozed.Value!.Nudge.Until);
        Assert.True(snoozed.Value.Nudge.Quiet);

        var on = await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.On, null, default);
        Assert.False(on.Value!.Nudge.Quiet);
        var stored = await setup.Settings.GetAsync<PersonNudgeSettings>(Owner, PersonNudgeSettings.Section, default);
        Assert.Empty(stored!.People!);
    }

    [Fact]
    public async Task Not_useful_quiets_for_longer_each_time_and_can_be_undone()
    {
        var setup = Create();

        var first = await setup.Service.NotUsefulAsync(Owner, setup.Person.Id, default);
        var second = await setup.Service.NotUsefulAsync(Owner, setup.Person.Id, default);

        Assert.Equal(Today.AddDays(30), first.Value!.Nudge.Until);
        Assert.Equal(Today.AddDays(60), second.Value!.Nudge.Until);
        Assert.Equal(2, second.Value.Nudge.NotUsefulCount);

        var undone = await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.On, null, default);
        Assert.False(undone.Value!.Nudge.Quiet);
        Assert.Equal(2, undone.Value.Nudge.NotUsefulCount);
    }

    [Fact]
    public async Task A_mute_stays_a_mute_when_marked_not_useful()
    {
        var setup = Create();
        await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.Muted, null, default);

        var result = await setup.Service.NotUsefulAsync(Owner, setup.Person.Id, default);

        Assert.Equal(NudgeModes.Muted, result.Value!.Nudge.Mode);
    }

    [Fact]
    public async Task Bad_input_and_other_owners_people_are_refused()
    {
        var setup = Create();

        Assert.Equal(PeopleFailure.Invalid,
            (await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, "loud", null, default)).Failure);
        Assert.Equal(PeopleFailure.Invalid,
            (await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.Snoozed, 0, default)).Failure);
        Assert.Equal(PeopleFailure.Invalid,
            (await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.Snoozed, 400, default)).Failure);
        Assert.Equal(PeopleFailure.NotFound,
            (await setup.Service.SetNudgeAsync(Other, setup.Person.Id, NudgeModes.Muted, null, default)).Failure);
        Assert.Equal(PeopleFailure.NotFound, (await setup.Service.NotUsefulAsync(Other, setup.Person.Id, default)).Failure);
        Assert.Null(await setup.Service.GetAsync(Other, setup.Person.Id, default));
    }

    [Fact]
    public async Task Another_owners_settings_do_not_quiet_this_owner()
    {
        var setup = Create();
        await setup.Settings.SaveAsync(Other, PersonNudgeSettings.Section,
            new PersonNudgeSettings(new() { [setup.Person.Id.ToString()] = new PersonNudgePref(NudgeModes.Muted) }),
            default);

        Assert.False((await setup.Service.GetAsync(Owner, setup.Person.Id, default))!.Nudge.Quiet);
    }

    [Fact]
    public async Task The_daily_check_in_skips_a_muted_person_but_still_nudges_the_rest()
    {
        var muted = Rosa(contacted: Noon.AddDays(-30));
        var other = muted with { Id = Guid.NewGuid(), Name = "Tom" };
        var repository = new PeopleTests.FakePeopleRepository();
        repository.People.AddRange([muted, other]);
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, PersonNudgeSettings.Section,
            new PersonNudgeSettings(new() { [muted.Id.ToString()] = new PersonNudgePref(NudgeModes.Muted) }), default);
        var titles = new List<string>();
        var notifications = Fake<INotificationRepository>.Create(("CreateAsync", args =>
        {
            titles.Add((string)args[2]!);
            return null;
        }));
        var service = new PeopleCheckInService(repository, notifications,
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null)), new FixedClock(Noon), null, settings);

        var result = await service.RunAsync(Owner, default);

        Assert.Equal(1, result.CheckInsNotified);
        Assert.Equal("Time to check in with Tom", Assert.Single(titles));
    }

    [Fact]
    public async Task The_tool_lists_facts_and_tells_the_model_not_to_push_a_snoozed_person()
    {
        var setup = Create([Promise("Rosa de Vries", "Send the deck", Today.AddDays(-1))]);
        await setup.Service.SetNudgeAsync(Owner, setup.Person.Id, NudgeModes.Snoozed, 10, default);
        var people = Fake<IPeopleService>.Create(("FindAsync", _ => setup.Person));
        var tools = new PersonFollowThroughTools(people, setup.Service, new FixedUser());

        var text = await tools.GetPersonFollowThroughAsync("rosa");

        Assert.Contains("User owes: Send the deck", text);
        Assert.Contains("[commitment:", text);
        Assert.Contains("snoozed suggestions", text);
        Assert.Contains("Do not push", text);
        Assert.Contains("not instructions", text);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
