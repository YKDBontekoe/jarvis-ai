using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Opportunities;

public static class OpportunityKinds
{
    public const string MeetingPrep = "meeting_prep";
    public const string FollowUp = "follow_up";
    public const string Renewal = "renewal";
    public const string DecisionReview = "decision_review";
    public const string FreeSlot = "free_slot";

    public static readonly string[] All = [MeetingPrep, FollowUp, Renewal, DecisionReview, FreeSlot];
}

public static class OpportunityVerdicts
{
    public const string Useful = "useful";
    public const string Later = "later";
    public const string Handled = "handled";
    public const string Wrong = "wrong";
    public const string Fewer = "fewer";

    public static readonly string[] All = [Useful, Later, Handled, Wrong, Fewer];
}

public sealed record MeetingFact(Guid Id, string Title, DateTimeOffset StartsAt, Guid? ProjectId, bool HasPrep);

public sealed record RenewalFact(Guid SubscriptionId, string Merchant, DateOnly NoticeOn, DateOnly NextDueOn,
    string Amount, bool Quiet);

public sealed record DecisionFact(Guid Id, string Title, DateOnly ReviewOn);

public sealed record FreeSlotFact(int Minutes, Guid ProjectId, string ProjectName, string NextAction);

/// <summary>The current facts the rules read. Gathered by the service; the engine does no I/O.</summary>
public sealed record OpportunityInputs(
    DateOnly Today,
    DateTimeOffset Now,
    IReadOnlyList<MeetingFact> Meetings,
    IReadOnlyList<Commitment> Promises,
    IReadOnlyList<RenewalFact> Renewals,
    IReadOnlyList<DecisionFact> Decisions,
    FreeSlotFact? FreeSlot);

/// <summary>
/// A chance to act while there is still time. <see cref="Key"/> is stable for the same facts (a changed date is a
/// new opportunity). <see cref="Evidence"/> are the facts it rests on and <see cref="Destination"/> is where to act.
/// </summary>
public sealed record Opportunity(
    string Key,
    string Kind,
    string GroupKey,
    string Title,
    string WhyNow,
    IReadOnlyList<string> Evidence,
    string Destination,
    int Urgency,
    DateOnly WindowEnds,
    IReadOnlyList<string> Also);

public sealed record OpportunityFeedback(string Verdict, DateOnly On, DateOnly? Until);

public sealed record OpportunityHistoryEntry(string Key, string Kind, string Title, DateOnly On);

/// <summary>Owner settings section <see cref="Section"/>: feedback, muted kinds and the accepted history.</summary>
public sealed record OpportunityState(
    Dictionary<string, OpportunityFeedback>? Feedback = null,
    Dictionary<string, DateOnly>? MutedKinds = null,
    Dictionary<string, int>? WrongCounts = null,
    IReadOnlyList<OpportunityHistoryEntry>? History = null)
{
    public const string Section = "opportunities";
    public const int MaxHistory = 30;
    public const int MaxFeedback = 300;
    public const int LaterDays = 3;
    public const int MuteDays = 30;
    public const int WrongBeforeMute = 2;

    public bool Hidden(string key, string kind, DateOnly today)
    {
        if (MutedKinds is not null && MutedKinds.TryGetValue(kind, out var until) && until > today) return true;
        if (Feedback is null || !Feedback.TryGetValue(key, out var feedback)) return false;
        return feedback.Verdict switch
        {
            OpportunityVerdicts.Later => feedback.Until > today,
            OpportunityVerdicts.Handled or OpportunityVerdicts.Wrong or OpportunityVerdicts.Useful => true,
            _ => false
        };
    }
}

public sealed record OpportunityList(IReadOnlyList<Opportunity> Items, IReadOnlyList<OpportunityHistoryEntry> History,
    IReadOnlyList<string> MutedKinds);

public sealed record OpportunityFeedbackRequest(string? Key, string? Verdict);

public interface IOpportunityService
{
    /// <summary>Evaluating reads facts and proposes; it never sends, creates or changes anything.</summary>
    Task<OpportunityList> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>False when the verdict is unknown or the opportunity is not currently on the radar.</summary>
    Task<bool> FeedbackAsync(Guid ownerId, string key, string verdict, CancellationToken cancellationToken);

    /// <summary>Lets a muted kind back in (undoes "fewer like this").</summary>
    Task<bool> UnmuteAsync(Guid ownerId, string kind, CancellationToken cancellationToken);
}
