using Jarvis.Application.Inbox;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Opportunities;

/// <summary>
/// Deterministic rules for chances worth acting on. Each has a reason it matters now and an eligibility window;
/// old facts never become urgent claims, related ones merge into one card, and feedback is applied last.
/// </summary>
public static class OpportunityEngine
{
    public const int MeetingHours = 24;
    public const int FollowUpAheadDays = 3;
    public const int StaleDays = 30;
    public const int RenewalAheadDays = 7;
    public const int DecisionAheadDays = 7;
    public const int FreeSlotMinutes = 45;
    public const int MaxItems = 8;

    public static IReadOnlyList<Opportunity> Evaluate(OpportunityInputs input, OpportunityState state)
    {
        var all = new List<Opportunity>();
        all.AddRange(MeetingPrep(input));
        all.AddRange(FollowUps(input));
        all.AddRange(Renewals(input));
        all.AddRange(Decisions(input));
        all.AddRange(FreeSlot(input));

        var visible = all.Where(item => !state.Hidden(item.Key, item.Kind, input.Today));
        // One card per project / person / subscription: the most urgent leads, the rest are listed under it.
        return visible.GroupBy(item => item.GroupKey)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(item => item.Urgency).ThenBy(item => item.WindowEnds).ToArray();
                return ordered[0] with { Also = ordered.Skip(1).Select(item => item.WhyNow).ToArray() };
            })
            .OrderByDescending(item => item.Urgency).ThenBy(item => item.WindowEnds).ThenBy(item => item.Title)
            .Take(MaxItems).ToArray();
    }

    private static IEnumerable<Opportunity> MeetingPrep(OpportunityInputs input) =>
        input.Meetings
            .Where(m => !m.HasPrep && m.StartsAt > input.Now && m.StartsAt <= input.Now.AddHours(MeetingHours))
            .Select(m =>
            {
                var hours = Math.Max(1, (int)Math.Ceiling((m.StartsAt - input.Now).TotalHours));
                return new Opportunity($"meeting_prep:{m.Id}", OpportunityKinds.MeetingPrep,
                    m.ProjectId is { } project ? $"project:{project}" : $"meeting:{m.Id}",
                    $"Prepare for “{m.Title}”",
                    $"It starts in about {hours} hour{(hours == 1 ? "" : "s")} and has no prep note yet.",
                    [$"meeting:{m.Id}"], $"meeting:{m.Id}", hours <= 3 ? 3 : 2,
                    DateOnly.FromDateTime(m.StartsAt.UtcDateTime), []);
            });

    private static IEnumerable<Opportunity> FollowUps(OpportunityInputs input)
    {
        foreach (var promise in input.Promises)
        {
            if (promise is not { Suggested: false, Status: CommitmentStatuses.Open, DueOn: { } due }) continue;
            var days = due.DayNumber - input.Today.DayNumber;
            if (days > FollowUpAheadDays) continue;
            var mine = promise.Direction == CommitmentDirections.IOwe;
            var group = promise.ProjectId is { } project ? $"project:{project}"
                : promise.PersonId is { } person ? $"person:{person}" : $"person:{promise.Counterparty.ToLowerInvariant()}";
            if (-days > StaleDays)
            {
                // Too old to call urgent: ask whether it still matters instead.
                yield return new Opportunity($"follow_up:{promise.Id}:stale", OpportunityKinds.FollowUp, group,
                    $"Is this still open? {promise.Description}",
                    $"It was due {-days} days ago. You may want to drop it or move the date.",
                    [$"commitment:{promise.Id}"], "inbox", 1, due, []);
                continue;
            }
            var when = days switch { < 0 => $"{-days} day{(days == -1 ? "" : "s")} late", 0 => "due today", _ => $"due in {days} day{(days == 1 ? "" : "s")}" };
            yield return new Opportunity($"follow_up:{promise.Id}:{due:yyyyMMdd}", OpportunityKinds.FollowUp, group,
                mine ? $"Plan time for: {promise.Description}" : $"Check in with {promise.Counterparty}: {promise.Description}",
                mine ? $"You promised this to {promise.Counterparty}; it is {when}." : $"{promise.Counterparty} promised this; it is {when}." +
                  (-days >= CommitmentFollowUpRules.MinDaysOverdue ? " A draft nudge is ready." : string.Empty),
                [$"commitment:{promise.Id}"], mine ? "plan-today" : "inbox", days <= 0 ? 3 : 2, due, []);
        }
    }

    private static IEnumerable<Opportunity> Renewals(OpportunityInputs input) =>
        input.Renewals
            .Where(r => !r.Quiet && r.NoticeOn >= input.Today.AddDays(-1) &&
                        r.NoticeOn <= input.Today.AddDays(RenewalAheadDays))
            .Select(r =>
            {
                var days = r.NoticeOn.DayNumber - input.Today.DayNumber;
                return new Opportunity($"renewal:{r.SubscriptionId}:{r.NextDueOn:yyyyMMdd}", OpportunityKinds.Renewal,
                    $"subscription:{r.SubscriptionId}", $"Review {r.Merchant} before it renews",
                    days <= 0
                        ? $"The next charge ({r.Amount}) is expected {r.NextDueOn:d MMM}. Today is the day to decide."
                        : $"The next charge ({r.Amount}) is expected {r.NextDueOn:d MMM}; decide by {r.NoticeOn:d MMM}. A comparison and a price pack can be prepared.",
                    [$"subscription:{r.SubscriptionId}"], $"subscription:{r.SubscriptionId}", days <= 1 ? 3 : 2,
                    r.NoticeOn, []);
            });

    private static IEnumerable<Opportunity> Decisions(OpportunityInputs input) =>
        input.Decisions
            .Where(d => d.ReviewOn <= input.Today.AddDays(DecisionAheadDays))
            .Select(d => new Opportunity($"decision_review:{d.Id}:{d.ReviewOn:yyyyMMdd}",
                OpportunityKinds.DecisionReview, $"decision:{d.Id}", $"Revisit: {d.Title}",
                d.ReviewOn <= input.Today
                    ? "You set this review day and it has come."
                    : $"You planned to review it on {d.ReviewOn:d MMM}.",
                [$"decision:{d.Id}"], "decisions", d.ReviewOn <= input.Today ? 2 : 1, d.ReviewOn, []));

    private static IEnumerable<Opportunity> FreeSlot(OpportunityInputs input)
    {
        if (input.FreeSlot is not { } slot || slot.Minutes < FreeSlotMinutes) yield break;
        yield return new Opportunity($"free_slot:{slot.ProjectId}:{input.Today:yyyyMMdd}", OpportunityKinds.FreeSlot,
            $"project:{slot.ProjectId}", $"Use your free time on {slot.ProjectName}",
            $"You have about {slot.Minutes} free minutes today, and the next step is: {slot.NextAction}",
            [$"project:{slot.ProjectId}"], $"project:{slot.ProjectId}", 1, input.Today, []);
    }

    /// <summary>Applies one piece of feedback and returns the new state. Pure so every verdict is tested.</summary>
    public static OpportunityState Apply(OpportunityState state, Opportunity item, string verdict, DateOnly today)
    {
        var feedback = new Dictionary<string, OpportunityFeedback>(state.Feedback ?? []);
        var muted = new Dictionary<string, DateOnly>(state.MutedKinds ?? []);
        var wrong = new Dictionary<string, int>(state.WrongCounts ?? []);
        var history = (state.History ?? []).ToList();
        feedback[item.Key] = new OpportunityFeedback(verdict, today,
            verdict == OpportunityVerdicts.Later ? today.AddDays(OpportunityState.LaterDays) : null);
        switch (verdict)
        {
            case OpportunityVerdicts.Useful:
                history.Insert(0, new OpportunityHistoryEntry(item.Key, item.Kind, item.Title, today));
                break;
            case OpportunityVerdicts.Fewer:
                muted[item.Kind] = today.AddDays(OpportunityState.MuteDays);
                break;
            case OpportunityVerdicts.Wrong:
                wrong[item.Kind] = wrong.GetValueOrDefault(item.Kind) + 1;
                if (wrong[item.Kind] >= OpportunityState.WrongBeforeMute)
                {
                    muted[item.Kind] = today.AddDays(OpportunityState.MuteDays);
                    wrong[item.Kind] = 0;
                }
                break;
        }
        // Keep the newest entries so the state stays small.
        var kept = feedback.OrderByDescending(pair => pair.Value.On).Take(OpportunityState.MaxFeedback)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return new OpportunityState(kept, muted, wrong, history.Take(OpportunityState.MaxHistory).ToArray());
    }
}
