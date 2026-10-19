using System.Globalization;
using System.Text;
using Jarvis.Application.Comparisons;
using Jarvis.Application.Inbox;
using Jarvis.Application.Library;
using Jarvis.Application.Settings;
using Jarvis.Domain.Finance;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Finance;

public static class ReviewChoices
{
    public const string Keep = "keep";
    public const string Investigate = "investigate";
    public const string Negotiate = "negotiate";
    public const string Cancel = "cancel";
    public const string Remind = "remind";
    public const string Archive = "archive";

    public static readonly string[] All = [Keep, Investigate, Negotiate, Cancel, Remind, Archive];
}

/// <summary>
/// What the owner decided and confirmed about one subscription. Nothing here is detected: a confirmed price or a
/// cancellation is only recorded when the owner says they have the merchant's confirmation.
/// </summary>
public sealed record SubscriptionReviewRecord(
    Guid SubscriptionId,
    string? Choice = null,
    DateOnly? ChoiceOn = null,
    DateOnly? KeepUntil = null,
    Guid? LibraryItemId = null,
    Guid? ComparisonId = null,
    Guid? FollowUpCommitmentId = null,
    decimal? ConfirmedAmount = null,
    DateOnly? ConfirmedOn = null,
    string? ConfirmedNote = null,
    string? CancelEvidence = null,
    DateOnly? CancelledOn = null);

/// <summary>Owner settings section <see cref="Section"/>: reviews by subscription id.</summary>
public sealed record SubscriptionReviewState(Dictionary<string, SubscriptionReviewRecord>? Reviews = null)
{
    public const string Section = "subscriptions.reviews";

    public SubscriptionReviewRecord For(Guid id) =>
        Reviews is not null && Reviews.TryGetValue(id.ToString(), out var record)
            ? record
            : new SubscriptionReviewRecord(id);
}

public sealed record ReviewPath(string Id, string Label, string NextAction);

/// <summary>A saving the owner confirmed: monthly equivalent and why it is counted that way.</summary>
public sealed record ConfirmedSaving(decimal OldAmount, decimal NewAmount, string Cadence, decimal PerMonth,
    DateOnly ConfirmedOn, string Basis);

public sealed record SubscriptionReviewView(
    Subscription Subscription,
    SubscriptionReviewRecord Review,
    decimal EffectiveAmount,
    DateOnly? NoticeOn,
    bool Quiet,
    IReadOnlyList<string> Facts,
    IReadOnlyList<ReviewPath> Paths,
    ConfirmedSaving? Saving);

public sealed record ReviewPackResult(Guid LibraryItemId, string Markdown);

public sealed record FollowUpResult(Guid CommitmentId, bool Created);

public static class SubscriptionReviewRules
{
    public const int KeepDays = 90;
    public const int MinEvidenceLength = 8;

    /// <summary>The day by which to act: the next charge minus the owner's reminder lead, or the charge day itself.</summary>
    public static DateOnly NoticeOn(Subscription subscription) =>
        subscription.NextDueOn.AddDays(-(subscription.RemindDaysBefore ?? 0));

    public static bool Quiet(SubscriptionReviewRecord review, DateOnly today) =>
        review.KeepUntil is { } until && until > today;

    public static IReadOnlyList<string> Facts(Subscription subscription, DateOnly today)
    {
        var facts = new List<string>
        {
            $"{Money(subscription.Amount)} {subscription.Currency} {subscription.Cadence}, about " +
            $"{Money(SubscriptionCadences.PerMonth(subscription.Amount, subscription.Cadence))} a month.",
            $"Charged {subscription.ChargeCount} time{(subscription.ChargeCount == 1 ? "" : "s")} in your bank data, last on " +
            $"{subscription.LastChargedOn:d MMM yyyy}; next expected {subscription.NextDueOn:d MMM yyyy} " +
            $"({Math.Max(0, subscription.NextDueOn.DayNumber - today.DayNumber)} days)."
        };
        if (subscription.PreviousAmount is { } before && before != subscription.Amount)
            facts.Add($"The price {(subscription.Amount > before ? "went up" : "went down")} from {Money(before)} to {Money(subscription.Amount)}.");
        facts.Add("The next charge date is an estimate from the pattern in your charges, not a date the merchant gave you.");
        return facts;
    }

    public static IReadOnlyList<ReviewPath> Paths(Subscription subscription, SubscriptionReviewRecord review,
        DateOnly today)
    {
        var notice = NoticeOn(subscription);
        return
        [
            new(ReviewChoices.Keep, "Keep it",
                $"Nothing to do. Jarvis stays quiet about it until {today.AddDays(KeepDays):d MMM yyyy}."),
            new(ReviewChoices.Investigate, "Look at alternatives",
                review.ComparisonId is null
                    ? "Start a comparison with this plan as the first option; add alternatives and what you find out."
                    : "Open the comparison you started and add what you learn."),
            new(ReviewChoices.Negotiate, "Ask for a lower price",
                "Draft a message or work through it live with Jarvis; you decide on every offer."),
            new(ReviewChoices.Cancel, "Cancel it",
                subscription.CancelUrl is null
                    ? "Save the cancel page, then draft the message or go through it live. It counts as cancelled only when you have the merchant's confirmation."
                    : "Draft the message or go through the saved cancel page live. It counts as cancelled only when you have the merchant's confirmation."),
            new(ReviewChoices.Remind, "Remind me",
                subscription.RemindDaysBefore is null
                    ? $"Get a reminder before the next charge (around {subscription.NextDueOn:d MMM})."
                    : $"A reminder is set for {notice:d MMM}."),
            new(ReviewChoices.Archive, "Not a subscription",
                "Stop tracking it. Scanning your charges again will not bring it back.")
        ];
    }

    /// <summary>
    /// A saving only exists once the owner confirmed a lower price. It is a monthly equivalent of one charge
    /// difference; a yearly figure is only that difference carried for twelve months if the price stays.
    /// </summary>
    public static ConfirmedSaving? Saving(Subscription subscription, SubscriptionReviewRecord review)
    {
        if (review.ConfirmedAmount is not { } confirmed || review.ConfirmedOn is not { } on ||
            confirmed >= subscription.Amount) return null;
        var perMonth = SubscriptionCadences.PerMonth(subscription.Amount, subscription.Cadence) -
                       SubscriptionCadences.PerMonth(confirmed, subscription.Cadence);
        return new ConfirmedSaving(subscription.Amount, confirmed, subscription.Cadence,
            Math.Round(perMonth, 2), on,
            $"You confirmed {Money(confirmed)} {subscription.Currency} {subscription.Cadence} on {on:d MMM yyyy}, " +
            $"against {Money(subscription.Amount)} before: {Money(perMonth)} a month. " +
            $"Over a year that is {Money(perMonth * 12)} if the price holds.");
    }

    /// <summary>The pack the owner takes into a negotiation or a cancellation. Account details stay placeholders.</summary>
    public static string Pack(Subscription subscription, string goal, DateOnly today, SubscriptionReviewRecord review)
    {
        var cancel = goal == NegotiationGoals.Cancel;
        var text = new StringBuilder();
        text.Append("# ").Append(cancel ? "Cancel" : "Lower the price of").Append(' ').AppendLine(subscription.Merchant);
        text.AppendLine().AppendLine($"Prepared {today:d MMM yyyy} from the owner's own bank data. Nothing was sent.");
        text.AppendLine().AppendLine("## What the records show");
        foreach (var fact in Facts(subscription, today)) text.Append("- ").AppendLine(fact);
        text.AppendLine().AppendLine("## Fill in before contacting them");
        text.AppendLine("- Account or customer number: [ADD]");
        text.AppendLine("- Name on the account: [ADD]");
        text.AppendLine("- Contract end date or notice period (check the contract, not this estimate): [ADD]");
        if (!cancel) text.AppendLine("- A cheaper alternative you found, with its source and date: [ADD]");
        text.AppendLine().AppendLine("## Ask");
        text.AppendLine(cancel
            ? "End the subscription at the end of the current period, confirm the end date in writing, and take no further charges."
            : "Ask for a lower price or a retention offer. Do not accept a longer contract without deciding yourself.");
        text.AppendLine().AppendLine("## Before you call it done");
        text.AppendLine(cancel
            ? "- [ ] You have a written confirmation (page, email or reference) with the end date.\n- [ ] You noted the reference below.\n- [ ] Only then mark it cancelled in Jarvis."
            : "- [ ] The offer is in writing with the exact price and the conditions.\n- [ ] You decided to accept it.\n- [ ] Only then record the confirmed price in Jarvis.");
        text.AppendLine().Append("Confirmation reference: ").AppendLine(review.CancelEvidence ?? "[ADD]");
        return text.ToString().TrimEnd();
    }

    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

public interface ISubscriptionReviewService
{
    Task<FinanceOperation<SubscriptionReviewView>> GetAsync(Guid ownerId, Guid subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>Records the owner's choice; keep, remind and archive also do what they say. The rest only note it.</summary>
    Task<FinanceOperation<SubscriptionReviewView>> ChooseAsync(Guid ownerId, Guid subscriptionId, string? choice,
        CancellationToken cancellationToken);

    /// <summary>Starts (once) a comparison with the current plan as its first option.</summary>
    Task<FinanceOperation<Guid>> StartComparisonAsync(Guid ownerId, Guid subscriptionId,
        CancellationToken cancellationToken);

    Task<FinanceOperation<ReviewPackResult>> BuildPackAsync(Guid ownerId, Guid subscriptionId, string? goal,
        Guid? projectId, CancellationToken cancellationToken);

    /// <summary>Tracks the provider's reply as an accepted promise owed to the owner (one per subscription).</summary>
    Task<FinanceOperation<FollowUpResult>> TrackResponseAsync(Guid ownerId, Guid subscriptionId, DateOnly? expectedBy,
        CancellationToken cancellationToken);

    /// <summary>The owner confirmed a lower price: it must be lower, and it only changes the review math.</summary>
    Task<FinanceOperation<SubscriptionReviewView>> RecordOutcomeAsync(Guid ownerId, Guid subscriptionId,
        decimal? confirmedAmount, DateOnly? confirmedOn, string? note, CancellationToken cancellationToken);

    /// <summary>Marks it cancelled, but only with the merchant's confirmation written down.</summary>
    Task<FinanceOperation<SubscriptionReviewView>> ConfirmCancellationAsync(Guid ownerId, Guid subscriptionId,
        string? evidence, DateOnly? effectiveOn, CancellationToken cancellationToken);
}

public sealed class SubscriptionReviewService(
    IFinanceRepository repository,
    IFinanceService finance,
    IOwnerSettingsStore settings,
    ILibraryService library,
    IComparisonService comparisons,
    ICommitmentService commitments,
    TimeProvider? timeProvider = null) : ISubscriptionReviewService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<FinanceOperation<SubscriptionReviewView>> GetAsync(Guid ownerId, Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<SubscriptionReviewView>.NotFound();
        return FinanceOperation<SubscriptionReviewView>.Ok(await ViewAsync(ownerId, subscription, cancellationToken));
    }

    public async Task<FinanceOperation<SubscriptionReviewView>> ChooseAsync(Guid ownerId, Guid subscriptionId,
        string? choice, CancellationToken cancellationToken)
    {
        if (choice is null || !ReviewChoices.All.Contains(choice))
            return FinanceOperation<SubscriptionReviewView>.Invalid("choice", "Choose keep, investigate, negotiate, cancel, remind or archive.");
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<SubscriptionReviewView>.NotFound();
        var today = await finance.TodayAsync(ownerId, cancellationToken);
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscriptionId) with
        {
            Choice = choice,
            ChoiceOn = today,
            KeepUntil = choice == ReviewChoices.Keep ? today.AddDays(SubscriptionReviewRules.KeepDays) : null
        };
        switch (choice)
        {
            case ReviewChoices.Remind:
                var reminded = await finance.UpdateSubscriptionAsync(subscriptionId, ownerId, null,
                    subscription.RemindDaysBefore ?? 3, false, today, cancellationToken);
                if (!reminded.Succeeded)
                    return FinanceOperation<SubscriptionReviewView>.Invalid(reminded.Field ?? "remind", reminded.Message ?? "Could not set the reminder.");
                subscription = reminded.Value!;
                break;
            case ReviewChoices.Archive:
                var archived = await finance.UpdateSubscriptionAsync(subscriptionId, ownerId,
                    SubscriptionStatuses.Dismissed, null, true, today, cancellationToken);
                if (!archived.Succeeded)
                    return FinanceOperation<SubscriptionReviewView>.Invalid(archived.Field ?? "archive", archived.Message ?? "Could not archive it.");
                subscription = archived.Value!;
                break;
        }
        await SaveAsync(ownerId, review, cancellationToken);
        return FinanceOperation<SubscriptionReviewView>.Ok(await ViewAsync(ownerId, subscription, cancellationToken));
    }

    public async Task<FinanceOperation<Guid>> StartComparisonAsync(Guid ownerId, Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<Guid>.NotFound();
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscriptionId);
        if (review.ComparisonId is { } existing && await comparisons.GetAsync(existing, ownerId, cancellationToken) is not null)
            return FinanceOperation<Guid>.Ok(existing);

        var definition = new ComparisonDefinition($"{subscription.Merchant}: stay or switch?",
            $"Is {subscription.Merchant} still the right plan at {SubscriptionReviewRules.Money(subscription.Amount)} {subscription.Currency} {subscription.Cadence}?",
            [new("price", "Price", 8), new("value", "How much I use it", 6), new("terms", "Terms and notice", 4)],
            [new("current", $"{subscription.Merchant} (now)", "Your current plan.",
                new Dictionary<string, ComparisonCell>
                {
                    ["price"] = new(null, $"{SubscriptionReviewRules.Money(subscription.Amount)} {subscription.Currency} {subscription.Cadence}",
                        "bank data", subscription.LastChargedOn)
                }),
             new("alternative", "An alternative", "Add what you find, with its source and date.",
                new Dictionary<string, ComparisonCell>())]);
        var created = await comparisons.CreateAsync(ownerId, definition, null, cancellationToken);
        if (!created.Succeeded)
            return FinanceOperation<Guid>.Invalid(created.Field ?? "comparison", created.Message ?? "Could not start the comparison.");
        await SaveAsync(ownerId, review with { ComparisonId = created.Value!.Record.Id }, cancellationToken);
        return FinanceOperation<Guid>.Ok(created.Value.Record.Id);
    }

    public async Task<FinanceOperation<ReviewPackResult>> BuildPackAsync(Guid ownerId, Guid subscriptionId,
        string? goal, Guid? projectId, CancellationToken cancellationToken)
    {
        goal ??= NegotiationGoals.LowerPrice;
        if (!NegotiationGoals.IsValid(goal))
            return FinanceOperation<ReviewPackResult>.Invalid("goal", "Use cancel or lower_price.");
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<ReviewPackResult>.NotFound();
        var today = await finance.TodayAsync(ownerId, cancellationToken);
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscriptionId);
        var markdown = SubscriptionReviewRules.Pack(subscription, goal, today, review);
        var saved = await library.AddNoteAsync(ownerId,
            new NoteDraft($"{(goal == NegotiationGoals.Cancel ? "Cancel" : "Negotiate")} {subscription.Merchant}",
                markdown, LibraryKinds.Report, null, ["subscription"], LibraryOrigins.App, projectId),
            cancellationToken);
        if (!saved.Succeeded)
            return FinanceOperation<ReviewPackResult>.Invalid(saved.Field ?? "library", saved.Message ?? "Could not save to the Library.");
        await SaveAsync(ownerId, review with { LibraryItemId = saved.Value!.Id }, cancellationToken);
        return FinanceOperation<ReviewPackResult>.Ok(new ReviewPackResult(saved.Value.Id, markdown));
    }

    public async Task<FinanceOperation<FollowUpResult>> TrackResponseAsync(Guid ownerId, Guid subscriptionId,
        DateOnly? expectedBy, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<FollowUpResult>.NotFound();
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscriptionId);
        if (review.FollowUpCommitmentId is { } existingId &&
            await commitments.GetAsync(existingId, ownerId, cancellationToken) is { Status: CommitmentStatuses.Open })
            return FinanceOperation<FollowUpResult>.Ok(new FollowUpResult(existingId, false));

        var today = await finance.TodayAsync(ownerId, cancellationToken);
        var what = subscription.NegotiationGoal == NegotiationGoals.Cancel ? "cancellation confirmation" : "reply about the price";
        var created = await commitments.CreateAsync(ownerId,
            new CommitmentDraft(CommitmentDirections.OwedToMe, subscription.Merchant,
                $"{subscription.Merchant}: {what}", expectedBy ?? today.AddDays(7), CommitmentSources.Manual),
            today, cancellationToken);
        if (!created.Succeeded)
            return FinanceOperation<FollowUpResult>.Invalid(created.Field ?? "expectedBy", created.Message ?? "Could not track the reply.");
        await SaveAsync(ownerId, review with { FollowUpCommitmentId = created.Value!.Id }, cancellationToken);
        return FinanceOperation<FollowUpResult>.Ok(new FollowUpResult(created.Value.Id, true));
    }

    public async Task<FinanceOperation<SubscriptionReviewView>> RecordOutcomeAsync(Guid ownerId, Guid subscriptionId,
        decimal? confirmedAmount, DateOnly? confirmedOn, string? note, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<SubscriptionReviewView>.NotFound();
        if (confirmedAmount is not { } amount || amount <= 0 || amount >= subscription.Amount)
            return FinanceOperation<SubscriptionReviewView>.Invalid("confirmedAmount",
                $"Enter the new price you have in writing; it must be lower than {SubscriptionReviewRules.Money(subscription.Amount)}.");
        var today = await finance.TodayAsync(ownerId, cancellationToken);
        if (confirmedOn is { } day && (day > today || day < today.AddYears(-1)))
            return FinanceOperation<SubscriptionReviewView>.Invalid("confirmedOn", "Pick the day you got the confirmation, within the last year.");
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscriptionId) with
        {
            ConfirmedAmount = amount,
            ConfirmedOn = confirmedOn ?? today,
            ConfirmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 300)]
        };
        await SaveAsync(ownerId, review, cancellationToken);
        if (review.FollowUpCommitmentId is { } follow)
            await commitments.SetStatusAsync(follow, ownerId, CommitmentStatuses.Done, cancellationToken);
        return FinanceOperation<SubscriptionReviewView>.Ok(await ViewAsync(ownerId, subscription, cancellationToken));
    }

    public async Task<FinanceOperation<SubscriptionReviewView>> ConfirmCancellationAsync(Guid ownerId,
        Guid subscriptionId, string? evidence, DateOnly? effectiveOn, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionAsync(subscriptionId, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<SubscriptionReviewView>.NotFound();
        var clean = string.Join(' ', (evidence ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length < SubscriptionReviewRules.MinEvidenceLength)
            return FinanceOperation<SubscriptionReviewView>.Invalid("evidence",
                "Write down the merchant's confirmation: a reference number, or the date and wording of their message.");
        var today = await finance.TodayAsync(ownerId, cancellationToken);
        var updated = await finance.UpdateSubscriptionAsync(subscriptionId, ownerId, SubscriptionStatuses.Cancelled,
            null, true, today, cancellationToken);
        if (!updated.Succeeded)
            return FinanceOperation<SubscriptionReviewView>.Invalid(updated.Field ?? "status", updated.Message ?? "Could not mark it cancelled.");
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscriptionId) with
        {
            CancelEvidence = clean[..Math.Min(clean.Length, 300)],
            CancelledOn = effectiveOn ?? today
        };
        await SaveAsync(ownerId, review, cancellationToken);
        if (review.FollowUpCommitmentId is { } follow)
            await commitments.SetStatusAsync(follow, ownerId, CommitmentStatuses.Done, cancellationToken);
        return FinanceOperation<SubscriptionReviewView>.Ok(await ViewAsync(ownerId, updated.Value!, cancellationToken));
    }

    private async Task<SubscriptionReviewView> ViewAsync(Guid ownerId, Subscription subscription,
        CancellationToken cancellationToken)
    {
        var today = await finance.TodayAsync(ownerId, cancellationToken);
        var review = (await LoadAsync(ownerId, cancellationToken)).For(subscription.Id);
        var saving = SubscriptionReviewRules.Saving(subscription, review);
        return new SubscriptionReviewView(subscription, review, saving?.NewAmount ?? subscription.Amount,
            SubscriptionReviewRules.NoticeOn(subscription), SubscriptionReviewRules.Quiet(review, today),
            SubscriptionReviewRules.Facts(subscription, today),
            SubscriptionReviewRules.Paths(subscription, review, today), saving);
    }

    private async Task<SubscriptionReviewState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<SubscriptionReviewState>(ownerId, SubscriptionReviewState.Section, cancellationToken) ??
        new SubscriptionReviewState();

    private async Task SaveAsync(Guid ownerId, SubscriptionReviewRecord review, CancellationToken cancellationToken)
    {
        var current = await LoadAsync(ownerId, cancellationToken);
        var map = new Dictionary<string, SubscriptionReviewRecord>(current.Reviews ?? [])
        {
            [review.SubscriptionId.ToString()] = review
        };
        await settings.SaveAsync(ownerId, SubscriptionReviewState.Section, new SubscriptionReviewState(map),
            cancellationToken);
    }
}
