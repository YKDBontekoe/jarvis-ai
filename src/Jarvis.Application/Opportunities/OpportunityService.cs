using Jarvis.Application.Decisions;
using Jarvis.Application.Finance;
using Jarvis.Application.Inbox;
using Jarvis.Application.Meetings;
using Jarvis.Application.Planner;
using Jarvis.Application.Projects;
using Jarvis.Application.Settings;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Opportunities;

/// <summary>
/// Gathers the current facts, runs the rules and applies the owner's feedback. Reading is all it does: nothing is
/// sent, created or changed until the owner opens the destination and acts there.
/// </summary>
public sealed class OpportunityService(
    IMeetingService meetings,
    ICommitmentService commitments,
    IFinanceRepository finance,
    IDecisionService decisions,
    IDayPlannerService planner,
    IProjectStore projects,
    IProjectFocusService projectFocus,
    IOwnerSettingsStore settings,
    TimeProvider? timeProvider = null) : IOpportunityService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<OpportunityList> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var input = await GatherAsync(ownerId, cancellationToken);
        var muted = (state.MutedKinds ?? []).Where(pair => pair.Value > input.Today).Select(pair => pair.Key).ToArray();
        return new OpportunityList(OpportunityEngine.Evaluate(input, state), state.History ?? [], muted);
    }

    public async Task<bool> FeedbackAsync(Guid ownerId, string key, string verdict,
        CancellationToken cancellationToken)
    {
        if (!OpportunityVerdicts.All.Contains(verdict)) return false;
        var state = await LoadAsync(ownerId, cancellationToken);
        var input = await GatherAsync(ownerId, cancellationToken);
        // Feedback only applies to what is on the radar right now, so a stale or invented key does nothing.
        var all = OpportunityEngine.Evaluate(input, new OpportunityState());
        var item = all.FirstOrDefault(entry => entry.Key == key);
        if (item is null) return false;
        await settings.SaveAsync(ownerId, OpportunityState.Section,
            OpportunityEngine.Apply(state, item, verdict, input.Today), cancellationToken);
        return true;
    }

    public async Task<bool> UnmuteAsync(Guid ownerId, string kind, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        if (state.MutedKinds is null || !state.MutedKinds.ContainsKey(kind)) return false;
        var muted = new Dictionary<string, DateOnly>(state.MutedKinds);
        muted.Remove(kind);
        await settings.SaveAsync(ownerId, OpportunityState.Section, state with { MutedKinds = muted },
            cancellationToken);
        return true;
    }

    private async Task<OpportunityInputs> GatherAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = await commitments.TodayAsync(ownerId, cancellationToken);

        var meetingFacts = new List<MeetingFact>();
        foreach (var summary in await meetings.ListAsync(ownerId, cancellationToken))
        {
            if (summary.StartsAt is not { } start || start <= now ||
                start > now.AddHours(OpportunityEngine.MeetingHours)) continue;
            var record = await meetings.GetAsync(summary.Id, ownerId, cancellationToken);
            if (record is not null)
                meetingFacts.Add(new MeetingFact(record.Id, record.Title, start, record.ProjectId,
                    record.Document.Prep is not null));
        }

        var promises = await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, false, cancellationToken);

        var reviews = (await settings.GetAsync<SubscriptionReviewState>(ownerId, SubscriptionReviewState.Section,
            cancellationToken)) ?? new SubscriptionReviewState();
        var renewals = (await finance.ListSubscriptionsAsync(ownerId, cancellationToken))
            .Where(item => item.Status == SubscriptionStatuses.Active)
            .Select(item => new RenewalFact(item.Id, item.Merchant, SubscriptionReviewRules.NoticeOn(item),
                item.NextDueOn, $"{SubscriptionReviewRules.Money(item.Amount)} {item.Currency}",
                SubscriptionReviewRules.Quiet(reviews.For(item.Id), today)))
            .ToArray();

        var due = (await decisions.ListAsync(ownerId, DecisionStatuses.Open, 100, cancellationToken))
            .Select(item => new DecisionFact(item.Id, item.Title, item.ReviewOn)).ToArray();

        return new OpportunityInputs(today, now, meetingFacts, promises, renewals, due,
            await FreeSlotAsync(ownerId, cancellationToken));
    }

    private async Task<FreeSlotFact?> FreeSlotAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        DayTimelineDto day;
        try
        {
            day = await planner.GetTodayAsync(ownerId, null, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
        {
            return null; // without the calendar there is no honest claim about free time
        }
        if (day.CalendarUnavailable || day.FreeMinutes < OpportunityEngine.FreeSlotMinutes) return null;
        foreach (var project in await projects.ListAsync(ownerId, cancellationToken))
        {
            if (project.Status != "active") continue;
            var next = await projectFocus.DescribeAsync(ownerId, project, cancellationToken);
            if (next is { State: ProjectNextActionStates.Open } && (next.Title ?? next.Note) is { Length: > 0 } text)
                return new FreeSlotFact(day.FreeMinutes, project.Id, project.Name, text);
        }
        return null;
    }

    private async Task<OpportunityState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<OpportunityState>(ownerId, OpportunityState.Section, cancellationToken) ??
        new OpportunityState();
}
