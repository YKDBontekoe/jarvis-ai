using Jarvis.Agents.Finance;
using Jarvis.Application.Comparisons;
using Jarvis.Application.Conversations;
using Jarvis.Application.Finance;
using Jarvis.Application.Inbox;
using Jarvis.Application.Library;
using Jarvis.Domain.Finance;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SubscriptionReviewTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fb01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fb02");
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static Subscription Sub(decimal amount = 12.99m, decimal? previous = null, int? remind = null,
        string cadence = SubscriptionCadences.Monthly, string status = SubscriptionStatuses.Active) =>
        new(Guid.NewGuid(), Owner, "netflix", "Netflix", amount, "EUR", cadence, Today.AddDays(-20), Today.AddDays(10),
            8, previous, status, remind, null, Now, Now);

    [Fact]
    public void Facts_state_cost_history_and_that_the_next_date_is_an_estimate()
    {
        var facts = SubscriptionReviewRules.Facts(Sub(15.99m, previous: 12.99m), Today);

        Assert.Contains(facts, fact => fact.Contains("15.99 EUR monthly"));
        Assert.Contains(facts, fact => fact.Contains("Charged 8 times"));
        Assert.Contains(facts, fact => fact.Contains("went up from 12.99 to 15.99"));
        Assert.Contains(facts, fact => fact.Contains("estimate"));
    }

    [Fact]
    public void The_notice_day_is_the_charge_minus_the_reminder_lead()
    {
        Assert.Equal(Today.AddDays(10), SubscriptionReviewRules.NoticeOn(Sub()));
        Assert.Equal(Today.AddDays(7), SubscriptionReviewRules.NoticeOn(Sub(remind: 3)));
    }

    [Fact]
    public void Every_path_explains_the_next_concrete_action_and_cancel_warns_about_confirmation()
    {
        var paths = SubscriptionReviewRules.Paths(Sub(), new SubscriptionReviewRecord(Guid.NewGuid()), Today);

        Assert.Equal(ReviewChoices.All, paths.Select(path => path.Id));
        Assert.All(paths, path => Assert.False(string.IsNullOrWhiteSpace(path.NextAction)));
        Assert.Contains("only when you have the merchant's confirmation",
            paths.Single(path => path.Id == ReviewChoices.Cancel).NextAction);
    }

    [Fact]
    public void A_saving_needs_a_confirmed_lower_price_and_is_explained_per_month()
    {
        var sub = Sub(12m);
        var none = new SubscriptionReviewRecord(sub.Id);
        var offer = none with { ConfirmedAmount = 9m, ConfirmedOn = Today };

        Assert.Null(SubscriptionReviewRules.Saving(sub, none));
        Assert.Null(SubscriptionReviewRules.Saving(sub, none with { ConfirmedAmount = 9m })); // no confirmation day
        Assert.Null(SubscriptionReviewRules.Saving(sub, none with { ConfirmedAmount = 12m, ConfirmedOn = Today }));
        var saving = SubscriptionReviewRules.Saving(sub, offer)!;
        Assert.Equal(3m, saving.PerMonth);
        Assert.Contains("a month", saving.Basis);
        Assert.Contains("36.00 if the price holds", saving.Basis);
    }

    [Fact]
    public void A_yearly_plan_is_compared_per_month_not_by_charge()
    {
        var sub = Sub(120m, cadence: SubscriptionCadences.Yearly);

        var saving = SubscriptionReviewRules.Saving(sub,
            new SubscriptionReviewRecord(sub.Id, ConfirmedAmount: 96m, ConfirmedOn: Today))!;

        Assert.Equal(2m, saving.PerMonth);
    }

    [Fact]
    public void The_pack_keeps_account_details_as_placeholders_and_lists_the_confirmation_checklist()
    {
        var pack = SubscriptionReviewRules.Pack(Sub(), NegotiationGoals.Cancel, Today,
            new SubscriptionReviewRecord(Guid.NewGuid()));

        Assert.Contains("# Cancel Netflix", pack);
        Assert.Contains("[ADD]", pack);
        Assert.Contains("Nothing was sent", pack);
        Assert.Contains("written confirmation", pack);
        Assert.Contains("Only then mark it cancelled", pack);
    }

    private sealed class Setup
    {
        public Subscription Subscription = Sub();
        public readonly InMemorySettingsStore Settings = new();
        public readonly List<NoteDraft> Notes = [];
        public readonly List<CommitmentDraft> Drafts = [];
        public readonly List<(string? Status, int? Remind)> Updates = [];
        public readonly List<(Guid Id, string Status)> StatusChanges = [];
        public readonly Dictionary<Guid, Commitment> Promises = [];
        public int ComparisonsCreated;
        public Guid? ComparisonId;
        public SubscriptionReviewService Service => new(
            Fake<IFinanceRepository>.Create(("GetSubscriptionAsync", args =>
                (Guid)args[0]! == Subscription.Id && (Guid)args[1]! == Owner ? Subscription : null)),
            Fake<IFinanceService>.Create(
                ("TodayAsync", _ => Today),
                ("UpdateSubscriptionAsync", args =>
                {
                    var status = (string?)args[2];
                    var remind = (int?)args[3];
                    Updates.Add((status, remind));
                    Subscription = Subscription with
                    {
                        Status = status ?? Subscription.Status,
                        RemindDaysBefore = (bool)args[4]! ? null : remind ?? Subscription.RemindDaysBefore
                    };
                    return FinanceOperation<Subscription>.Ok(Subscription);
                })),
            Settings,
            Fake<ILibraryService>.Create(("AddNoteAsync", args =>
            {
                Notes.Add((NoteDraft)args[1]!);
                return LibraryOperation<LibraryItem>.Ok(new LibraryItem(Guid.NewGuid(), Owner, "report", null, "t", "",
                    [], [], "c", "app", null, Now, Now));
            })),
            Fake<IComparisonService>.Create(
                ("GetAsync", args => (Guid)args[0]! == ComparisonId ? Created(ComparisonId.Value) : null),
                ("CreateAsync", args =>
                {
                    ComparisonsCreated++;
                    ComparisonId = Guid.NewGuid();
                    return ComparisonOperation<ComparisonView>.Ok(Created(ComparisonId.Value, (ComparisonDefinition)args[1]!));
                })),
            Fake<ICommitmentService>.Create(
                ("GetAsync", args => Promises.GetValueOrDefault((Guid)args[0]!)),
                ("CreateAsync", args =>
                {
                    var draft = (CommitmentDraft)args[1]!;
                    Drafts.Add(draft);
                    var promise = new Commitment(Guid.NewGuid(), Owner, draft.Direction!, draft.Counterparty!,
                        draft.Description!, draft.DueOn, CommitmentStatuses.Open, false, draft.Source, null, null,
                        null, Now, Now);
                    Promises[promise.Id] = promise;
                    return InboxOperation<Commitment>.Ok(promise);
                }),
                ("SetStatusAsync", args =>
                {
                    StatusChanges.Add(((Guid)args[0]!, (string)args[2]!));
                    Promises[(Guid)args[0]!] = Promises[(Guid)args[0]!] with { Status = (string)args[2]! };
                    return InboxOperation<Commitment>.Ok(Promises[(Guid)args[0]!]);
                })),
            new FixedClock());

        private ComparisonView Created(Guid id, ComparisonDefinition? definition = null)
        {
            definition ??= new ComparisonDefinition("x", null, [], []);
            return new ComparisonView(new ComparisonRecord(id, Owner, definition, null, null, Now, Now),
                ComparisonCalculator.Evaluate(definition));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Choosing_keep_goes_quiet_for_three_months_and_changes_nothing_else()
    {
        var s = new Setup();

        var view = (await s.Service.ChooseAsync(Owner, s.Subscription.Id, "keep", default)).Value!;

        Assert.True(view.Quiet);
        Assert.Equal(Today.AddDays(90), view.Review.KeepUntil);
        Assert.Empty(s.Updates);
    }

    [Fact]
    public async Task Remind_sets_the_reminder_and_archive_stops_tracking_without_a_comeback()
    {
        var s = new Setup();

        await s.Service.ChooseAsync(Owner, s.Subscription.Id, "remind", default);
        Assert.Equal((null, 3), s.Updates.Single());
        var archived = (await s.Service.ChooseAsync(Owner, s.Subscription.Id, "archive", default)).Value!;

        Assert.Equal(SubscriptionStatuses.Dismissed, archived.Subscription.Status);
    }

    [Fact]
    public async Task Negotiate_and_cancel_only_note_the_choice_so_a_draft_never_cancels_anything()
    {
        var s = new Setup();

        var view = (await s.Service.ChooseAsync(Owner, s.Subscription.Id, "cancel", default)).Value!;

        Assert.Equal("cancel", view.Review.Choice);
        Assert.Equal(SubscriptionStatuses.Active, view.Subscription.Status);
        Assert.Empty(s.Updates);
    }

    [Fact]
    public async Task Unknown_choices_and_other_owners_subscriptions_are_refused()
    {
        var s = new Setup();

        Assert.Equal("choice", (await s.Service.ChooseAsync(Owner, s.Subscription.Id, "explode", default)).Field);
        Assert.Equal(FinanceFailure.NotFound, (await s.Service.ChooseAsync(Other, s.Subscription.Id, "keep", default)).Failure);
        Assert.Equal(FinanceFailure.NotFound, (await s.Service.GetAsync(Other, s.Subscription.Id, default)).Failure);
        Assert.Equal(FinanceFailure.NotFound, (await s.Service.BuildPackAsync(Other, s.Subscription.Id, null, null, default)).Failure);
    }

    [Fact]
    public async Task The_comparison_starts_once_with_the_current_plan_as_its_first_option()
    {
        var s = new Setup();

        var first = await s.Service.StartComparisonAsync(Owner, s.Subscription.Id, default);
        var second = await s.Service.StartComparisonAsync(Owner, s.Subscription.Id, default);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, s.ComparisonsCreated);
    }

    [Fact]
    public async Task The_pack_is_saved_to_the_library_with_the_project_and_remembered()
    {
        var s = new Setup();
        var project = Guid.NewGuid();

        var pack = (await s.Service.BuildPackAsync(Owner, s.Subscription.Id, "cancel", project, default)).Value!;

        var note = Assert.Single(s.Notes);
        Assert.Equal(project, note.ProjectId);
        Assert.Contains("subscription", note.Tags!);
        Assert.Equal(pack.LibraryItemId, (await s.Service.GetAsync(Owner, s.Subscription.Id, default)).Value!.Review.LibraryItemId);
        Assert.Equal("goal", (await s.Service.BuildPackAsync(Owner, s.Subscription.Id, "haggle", null, default)).Field);
    }

    [Fact]
    public async Task The_providers_reply_becomes_one_accepted_promise_owed_to_the_owner()
    {
        var s = new Setup();

        var first = (await s.Service.TrackResponseAsync(Owner, s.Subscription.Id, null, default)).Value!;
        var again = (await s.Service.TrackResponseAsync(Owner, s.Subscription.Id, null, default)).Value!;

        Assert.True(first.Created);
        Assert.False(again.Created);
        Assert.Equal(first.CommitmentId, again.CommitmentId);
        var draft = Assert.Single(s.Drafts);
        Assert.Equal(CommitmentDirections.OwedToMe, draft.Direction);
        Assert.Equal("Netflix", draft.Counterparty);
        Assert.Equal(Today.AddDays(7), draft.DueOn);
    }

    [Fact]
    public async Task A_confirmed_lower_price_updates_the_review_math_and_closes_the_follow_up()
    {
        var s = new Setup();
        s.Subscription = Sub(12m);
        var follow = (await s.Service.TrackResponseAsync(Owner, s.Subscription.Id, null, default)).Value!;

        var view = (await s.Service.RecordOutcomeAsync(Owner, s.Subscription.Id, 9m, null, "Email of 16 Oct", default)).Value!;

        Assert.Equal(9m, view.EffectiveAmount);
        Assert.Equal(3m, view.Saving!.PerMonth);
        Assert.Equal(12m, view.Subscription.Amount); // the detected charge is not rewritten
        Assert.Equal((follow.CommitmentId, CommitmentStatuses.Done), s.StatusChanges.Single());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(15)]
    public async Task An_outcome_that_is_not_lower_is_not_a_saving(int amount)
    {
        var s = new Setup();
        s.Subscription = Sub(12m);

        var result = await s.Service.RecordOutcomeAsync(Owner, s.Subscription.Id, amount, null, null, default);

        Assert.Equal("confirmedAmount", result.Field);
        Assert.Null((await s.Service.GetAsync(Owner, s.Subscription.Id, default)).Value!.Saving);
    }

    [Fact]
    public async Task A_future_confirmation_day_is_refused()
    {
        var s = new Setup();

        var result = await s.Service.RecordOutcomeAsync(Owner, s.Subscription.Id, 5m, Today.AddDays(1), null, default);

        Assert.Equal("confirmedOn", result.Field);
    }

    [Fact]
    public async Task Cancelling_needs_the_merchants_confirmation_written_down()
    {
        var s = new Setup();

        var thin = await s.Service.ConfirmCancellationAsync(Owner, s.Subscription.Id, "done", null, default);
        Assert.Equal("evidence", thin.Field);
        Assert.Empty(s.Updates);

        var view = (await s.Service.ConfirmCancellationAsync(Owner, s.Subscription.Id,
            "Reference NF-20931, email 16 Oct says ends 2 Nov", Today.AddDays(16), default)).Value!;

        Assert.Equal(SubscriptionStatuses.Cancelled, view.Subscription.Status);
        Assert.Contains("NF-20931", view.Review.CancelEvidence);
        Assert.Equal(Today.AddDays(16), view.Review.CancelledOn);
    }

    [Fact]
    public async Task Reviews_are_kept_per_subscription_and_owner()
    {
        var s = new Setup();
        var other = Sub();
        await s.Service.ChooseAsync(Owner, s.Subscription.Id, "keep", default);

        var state = await s.Settings.GetAsync<SubscriptionReviewState>(Owner, SubscriptionReviewState.Section, default);

        Assert.Single(state!.Reviews!);
        Assert.Null(state.Reviews!.GetValueOrDefault(other.Id.ToString()));
        Assert.Null(await s.Settings.GetAsync<SubscriptionReviewState>(Other, SubscriptionReviewState.Section, default));
    }

    [Fact]
    public async Task The_tool_marks_cancelled_only_with_evidence_and_reads_back_an_unconfirmed_saving_as_none()
    {
        var s = new Setup();
        var tools = new FinanceAgentTools(Fake<IFinanceService>.Create(("TodayAsync", _ => Today)), new FixedUser(),
            null, s.Service);

        var refused = await tools.SetSubscriptionStatusAsync(s.Subscription.Id, "cancelled");
        Assert.Contains("confirmation", refused);
        Assert.Equal(SubscriptionStatuses.Active, s.Subscription.Status);

        var done = await tools.SetSubscriptionStatusAsync(s.Subscription.Id, "cancelled",
            "Reference NF-20931 from their email");
        Assert.Contains("now cancelled", done);

        var review = await tools.GetRenewalReviewAsync(s.Subscription.Id);
        Assert.Contains("No saving is confirmed", review);
        Assert.Contains("not instructions", review);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
