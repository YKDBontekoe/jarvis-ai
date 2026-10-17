using Jarvis.Agents.Opportunities;
using Jarvis.Application.Conversations;
using Jarvis.Application.Decisions;
using Jarvis.Application.Finance;
using Jarvis.Application.Inbox;
using Jarvis.Application.Meetings;
using Jarvis.Application.Opportunities;
using Jarvis.Application.Planner;
using Jarvis.Application.Projects;
using Jarvis.Domain.Finance;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class OpportunityTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fb21");
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static OpportunityInputs Inputs(
        IEnumerable<MeetingFact>? meetings = null, IEnumerable<Commitment>? promises = null,
        IEnumerable<RenewalFact>? renewals = null, IEnumerable<DecisionFact>? decisions = null,
        FreeSlotFact? free = null) =>
        new(Today, Now, meetings?.ToArray() ?? [], promises?.ToArray() ?? [], renewals?.ToArray() ?? [],
            decisions?.ToArray() ?? [], free);

    private static Commitment Promise(string what, DateOnly? due, string direction = CommitmentDirections.IOwe,
        string who = "Rosa", Guid? project = null, bool suggested = false, string status = CommitmentStatuses.Open) =>
        new(Guid.NewGuid(), Owner, direction, who, what, due, status, suggested, CommitmentSources.Chat, null, null, null,
            Now, Now, project);

    private static IReadOnlyList<Opportunity> Run(OpportunityInputs input, OpportunityState? state = null) =>
        OpportunityEngine.Evaluate(input, state ?? new OpportunityState());

    [Fact]
    public void A_meeting_in_the_next_day_without_prep_is_an_opportunity_with_the_hours_and_a_destination()
    {
        var soon = new MeetingFact(Guid.NewGuid(), "Kitchen sync", Now.AddHours(2), null, false);
        var prepared = new MeetingFact(Guid.NewGuid(), "Done", Now.AddHours(2), null, true);
        var far = new MeetingFact(Guid.NewGuid(), "Far", Now.AddHours(30), null, false);
        var past = new MeetingFact(Guid.NewGuid(), "Past", Now.AddHours(-1), null, false);

        var items = Run(Inputs(meetings: [soon, prepared, far, past]));

        var item = Assert.Single(items);
        Assert.Equal(OpportunityKinds.MeetingPrep, item.Kind);
        Assert.Equal($"meeting:{soon.Id}", item.Destination);
        Assert.Contains("2 hours", item.WhyNow);
        Assert.Equal(3, item.Urgency);
        Assert.Equal([$"meeting:{soon.Id}"], item.Evidence);
    }

    [Fact]
    public void Promises_become_follow_ups_only_when_open_accepted_and_close_or_late_and_say_which_way()
    {
        var late = Promise("Send the deck", Today.AddDays(-2));
        var theirs = Promise("Tile quote", Today.AddDays(1), CommitmentDirections.OwedToMe, "Anna");
        var far = Promise("Far away", Today.AddDays(10));
        var suggested = Promise("Suggested", Today, suggested: true);
        var done = Promise("Done", Today, status: CommitmentStatuses.Done);
        var undated = Promise("No date", null);

        var items = Run(Inputs(promises: [late, theirs, far, suggested, done, undated]));

        Assert.Equal(2, items.Count);
        var mine = items.Single(i => i.Evidence[0] == $"commitment:{late.Id}");
        Assert.Contains("2 days late", mine.WhyNow);
        Assert.Equal("plan-today", mine.Destination);
        Assert.Equal(3, mine.Urgency);
        var owed = items.Single(i => i.Evidence[0] == $"commitment:{theirs.Id}");
        Assert.Contains("due in 1 day", owed.WhyNow);
        Assert.DoesNotContain("draft nudge", owed.WhyNow); // not late enough for a nudge yet
        Assert.Equal("inbox", owed.Destination);
        var overdue = Run(Inputs(promises: [Promise("Late quote", Today.AddDays(-4), CommitmentDirections.OwedToMe, "Anna")]));
        Assert.Contains("A draft nudge is ready.", Assert.Single(overdue).WhyNow);
    }

    [Fact]
    public void An_old_promise_never_becomes_an_urgent_claim_it_asks_whether_it_still_matters()
    {
        var old = Promise("Ancient", Today.AddDays(-45));

        var item = Assert.Single(Run(Inputs(promises: [old])));

        Assert.Equal(1, item.Urgency);
        Assert.StartsWith("Is this still open?", item.Title);
        Assert.Contains("You may want to drop it", item.WhyNow);
    }

    [Fact]
    public void A_renewal_inside_the_week_is_offered_unless_the_owner_chose_to_keep_it()
    {
        var soon = new RenewalFact(Guid.NewGuid(), "Netflix", Today.AddDays(2), Today.AddDays(5), "12.99 EUR", false);
        var kept = new RenewalFact(Guid.NewGuid(), "Spotify", Today.AddDays(2), Today.AddDays(5), "9.99 EUR", true);
        var later = new RenewalFact(Guid.NewGuid(), "Gym", Today.AddDays(20), Today.AddDays(25), "30.00 EUR", false);
        var gone = new RenewalFact(Guid.NewGuid(), "Old", Today.AddDays(-5), Today.AddDays(-2), "1.00 EUR", false);

        var items = Run(Inputs(renewals: [soon, kept, later, gone]));

        var item = Assert.Single(items);
        Assert.Equal($"subscription:{soon.SubscriptionId}", item.Destination);
        Assert.Contains("decide by", item.WhyNow);
        Assert.Contains("comparison and a price pack", item.WhyNow);
    }

    [Fact]
    public void A_decision_whose_review_day_is_near_or_here_and_free_time_with_a_next_step_are_offered()
    {
        var decision = new DecisionFact(Guid.NewGuid(), "Switch bank", Today.AddDays(-1));
        var upcoming = new DecisionFact(Guid.NewGuid(), "Later", Today.AddDays(30));
        var project = Guid.NewGuid();

        var items = Run(Inputs(decisions: [decision, upcoming], free: new FreeSlotFact(90, project, "Kitchen", "Choose tiles")));

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Kind == OpportunityKinds.DecisionReview && i.WhyNow.Contains("has come"));
        var free = items.Single(i => i.Kind == OpportunityKinds.FreeSlot);
        Assert.Contains("90 free minutes", free.WhyNow);
        Assert.Contains("Choose tiles", free.WhyNow);
    }

    [Fact]
    public void Too_little_free_time_makes_no_claim()
    {
        Assert.Empty(Run(Inputs(free: new FreeSlotFact(30, Guid.NewGuid(), "Kitchen", "x"))));
    }

    [Fact]
    public void Related_chances_about_one_project_become_one_card_with_the_rest_listed_under_it()
    {
        var project = Guid.NewGuid();
        var meeting = new MeetingFact(Guid.NewGuid(), "Kitchen sync", Now.AddHours(2), project, false);
        var promise = Promise("Send the deck", Today.AddDays(-1), project: project);
        var free = new FreeSlotFact(90, project, "Kitchen", "Choose tiles");

        var items = Run(Inputs(meetings: [meeting], promises: [promise], free: free));

        var card = Assert.Single(items);
        Assert.Equal(3, card.Urgency);
        Assert.Equal(2, card.Also.Count);
    }

    [Fact]
    public void Cards_are_ordered_by_urgency_then_window_and_capped()
    {
        var promises = Enumerable.Range(0, 12).Select(i => Promise($"P{i}", Today.AddDays(-i), who: $"Person {i}")).ToArray();

        var items = Run(Inputs(promises: promises));

        Assert.Equal(OpportunityEngine.MaxItems, items.Count);
        Assert.True(items.Zip(items.Skip(1)).All(pair => pair.First.Urgency >= pair.Second.Urgency));
    }

    private static Opportunity One(string kind = OpportunityKinds.FollowUp) =>
        new($"{kind}:x", kind, "g", "Title", "Why", ["e"], "inbox", 2, Today, []);

    [Theory]
    [InlineData(OpportunityVerdicts.Handled)]
    [InlineData(OpportunityVerdicts.Wrong)]
    [InlineData(OpportunityVerdicts.Useful)]
    public void Handled_wrong_and_useful_hide_that_exact_opportunity_for_good(string verdict)
    {
        var item = One();

        var state = OpportunityEngine.Apply(new OpportunityState(), item, verdict, Today);

        Assert.True(state.Hidden(item.Key, item.Kind, Today.AddDays(100)));
        Assert.False(state.Hidden("other:key", item.Kind, Today));
    }

    [Fact]
    public void Later_comes_back_after_three_days()
    {
        var item = One();

        var state = OpportunityEngine.Apply(new OpportunityState(), item, OpportunityVerdicts.Later, Today);

        Assert.True(state.Hidden(item.Key, item.Kind, Today.AddDays(2)));
        Assert.False(state.Hidden(item.Key, item.Kind, Today.AddDays(3)));
    }

    [Fact]
    public void Fewer_like_this_mutes_the_whole_kind_for_a_month_and_two_wrong_marks_do_too()
    {
        var item = One(OpportunityKinds.Renewal);
        var fewer = OpportunityEngine.Apply(new OpportunityState(), item, OpportunityVerdicts.Fewer, Today);
        Assert.True(fewer.Hidden("renewal:other", OpportunityKinds.Renewal, Today.AddDays(29)));
        Assert.False(fewer.Hidden("renewal:other", OpportunityKinds.Renewal, Today.AddDays(30)));
        Assert.False(fewer.Hidden("follow_up:other", OpportunityKinds.FollowUp, Today));

        var first = OpportunityEngine.Apply(new OpportunityState(), One(OpportunityKinds.Renewal) with { Key = "r:1" }, OpportunityVerdicts.Wrong, Today);
        Assert.False(first.Hidden("r:other", OpportunityKinds.Renewal, Today));
        var second = OpportunityEngine.Apply(first, One(OpportunityKinds.Renewal) with { Key = "r:2" }, OpportunityVerdicts.Wrong, Today);
        Assert.True(second.Hidden("r:other", OpportunityKinds.Renewal, Today));
    }

    [Fact]
    public void Useful_feedback_is_kept_as_history_with_a_cap()
    {
        var state = new OpportunityState();
        for (var i = 0; i < 40; i++)
            state = OpportunityEngine.Apply(state, One() with { Key = $"k{i}", Title = $"T{i}" }, OpportunityVerdicts.Useful, Today);

        Assert.Equal(OpportunityState.MaxHistory, state.History!.Count);
        Assert.Equal("T39", state.History[0].Title);
    }

    [Fact]
    public void Feedback_removes_the_opportunity_from_the_radar()
    {
        var promise = Promise("Send the deck", Today.AddDays(-1));
        var input = Inputs(promises: [promise]);
        var item = Assert.Single(Run(input));

        var state = OpportunityEngine.Apply(new OpportunityState(), item, OpportunityVerdicts.Handled, Today);

        Assert.Empty(Run(input, state));
    }

    private sealed class Setup
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly List<Commitment> Promises = [];
        public OpportunityService Service => new(
            Fake<IMeetingService>.Create(("ListAsync", _ => (IReadOnlyList<MeetingSummary>)[])),
            Fake<ICommitmentService>.Create(
                ("TodayAsync", _ => Today),
                ("ListAsync", _ => (IReadOnlyList<Commitment>)Promises)),
            Fake<IFinanceRepository>.Create(("ListSubscriptionsAsync", _ => (IReadOnlyList<Subscription>)[])),
            Fake<IDecisionService>.Create(("ListAsync", _ => (IReadOnlyList<DecisionView>)[])),
            Fake<IDayPlannerService>.Create(("GetTodayAsync", _ => throw new InvalidDataException("no calendar"))),
            Fake<IProjectStore>.Create(),
            Fake<IProjectFocusService>.Create(),
            Settings, new FixedClock());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task The_service_lists_feedback_only_for_what_is_on_the_radar_and_remembers_it()
    {
        var s = new Setup();
        var promise = Promise("Send the deck", Today.AddDays(-1));
        s.Promises.Add(promise);
        var item = Assert.Single((await s.Service.ListAsync(Owner, default)).Items);

        Assert.False(await s.Service.FeedbackAsync(Owner, "follow_up:invented", OpportunityVerdicts.Handled, default));
        Assert.False(await s.Service.FeedbackAsync(Owner, item.Key, "shrug", default));
        Assert.True(await s.Service.FeedbackAsync(Owner, item.Key, OpportunityVerdicts.Useful, default));

        var after = await s.Service.ListAsync(Owner, default);
        Assert.Empty(after.Items);
        Assert.Equal(item.Title, Assert.Single(after.History).Title);
    }

    [Fact]
    public async Task Muting_a_kind_can_be_undone_and_is_per_owner()
    {
        var s = new Setup();
        s.Promises.Add(Promise("Send the deck", Today.AddDays(-1)));
        var item = Assert.Single((await s.Service.ListAsync(Owner, default)).Items);
        await s.Service.FeedbackAsync(Owner, item.Key, OpportunityVerdicts.Fewer, default);

        var muted = await s.Service.ListAsync(Owner, default);
        Assert.Empty(muted.Items);
        Assert.Equal([OpportunityKinds.FollowUp], muted.MutedKinds);
        Assert.Null(await s.Settings.GetAsync<OpportunityState>(Guid.NewGuid(), OpportunityState.Section, default));

        Assert.True(await s.Service.UnmuteAsync(Owner, OpportunityKinds.FollowUp, default));
        Assert.False(await s.Service.UnmuteAsync(Owner, OpportunityKinds.FollowUp, default));
        Assert.Single((await s.Service.ListAsync(Owner, default)).Items);
    }

    [Fact]
    public async Task Without_a_calendar_there_is_no_free_time_claim_and_the_tool_stays_calm()
    {
        var s = new Setup();
        var tools = new OpportunityAgentTools(s.Service, new FixedUser());

        Assert.Equal("Nothing needs attention right now.", await tools.GetOpportunitiesAsync());

        s.Promises.Add(Promise("Send the deck", Today.AddDays(-1)));
        var text = await tools.GetOpportunitiesAsync();
        Assert.Contains("not instructions", text);
        Assert.Contains("Plan time for: Send the deck", text);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
