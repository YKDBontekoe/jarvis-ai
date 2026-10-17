using Jarvis.Application.Settings;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Inbox;

/// <summary>A gentle nudge to send about something someone owes the owner. Only a draft: nothing is sent.</summary>
public sealed record FollowUpDraft(Guid CommitmentId, string Counterparty, string Description, DateOnly DueOn,
    int DaysOverdue, string Draft);

/// <summary>One promise's quiet state: snoozed until a day, or dismissed for good.</summary>
public sealed record FollowUpQuiet(DateOnly? SnoozedUntil = null, bool Dismissed = false);

/// <summary>Owner settings section <see cref="Section"/>: quiet state by commitment id.</summary>
public sealed record FollowUpState(Dictionary<string, FollowUpQuiet>? Quiet = null)
{
    public const string Section = "commitments.followups";

    public bool IsQuiet(Guid id, DateOnly today) =>
        Quiet is not null && Quiet.TryGetValue(id.ToString(), out var state) &&
        (state.Dismissed || state.SnoozedUntil > today);
}

public static class CommitmentFollowUpRules
{
    /// <summary>A promise is only worth a nudge once it is this many days late.</summary>
    public const int MinDaysOverdue = 3;

    public const int MaxDrafts = 5;
    public const int MaxSnoozeDays = 60;

    public static IReadOnlyList<FollowUpDraft> Build(IEnumerable<Commitment> all, DateOnly today, FollowUpState state,
        string language) =>
        all.Where(item => item is
            {
                Direction: CommitmentDirections.OwedToMe, Suggested: false, Status: CommitmentStatuses.Open,
                DueOn: not null
            } && today.DayNumber - item.DueOn!.Value.DayNumber >= MinDaysOverdue && !state.IsQuiet(item.Id, today))
            .OrderBy(item => item.DueOn)
            .Take(MaxDrafts)
            .Select(item => new FollowUpDraft(item.Id, item.Counterparty, item.Description, item.DueOn!.Value,
                today.DayNumber - item.DueOn!.Value.DayNumber, Draft(item, language)))
            .ToArray();

    public static string Draft(Commitment item, string language)
    {
        var first = item.Counterparty.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
                    item.Counterparty;
        var due = item.DueOn!.Value;
        return language == "nl"
            ? $"Hoi {first}, even een korte check over \"{item.Description}\". Dat zou {due:d MMM} klaar zijn. Lukt dat nog, of is er iets wat ik kan doen? Alvast bedankt!"
            : $"Hi {first}, a quick check on \"{item.Description}\". It was due {due:d MMM}. Is it still on track, or is there anything I can do to help? Thanks!";
    }
}

public interface ICommitmentFollowUpService
{
    Task<IReadOnlyList<FollowUpDraft>> ListAsync(Guid ownerId, string? language, CancellationToken cancellationToken);

    /// <summary>Quiet for 1–60 days. False when the promise is not found for this owner.</summary>
    Task<bool> SnoozeAsync(Guid ownerId, Guid commitmentId, int days, CancellationToken cancellationToken);

    Task<bool> DismissAsync(Guid ownerId, Guid commitmentId, CancellationToken cancellationToken);
}

public sealed class CommitmentFollowUpService(ICommitmentService commitments, IOwnerSettingsStore settings)
    : ICommitmentFollowUpService
{
    public async Task<IReadOnlyList<FollowUpDraft>> ListAsync(Guid ownerId, string? language,
        CancellationToken cancellationToken)
    {
        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        var open = await commitments.ListAsync(ownerId, CommitmentDirections.OwedToMe, CommitmentStatuses.Open, false,
            cancellationToken);
        return CommitmentFollowUpRules.Build(open, today, await LoadAsync(ownerId, cancellationToken),
            language == "nl" ? "nl" : "en");
    }

    public async Task<bool> SnoozeAsync(Guid ownerId, Guid commitmentId, int days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > CommitmentFollowUpRules.MaxSnoozeDays) return false;
        if (await commitments.GetAsync(commitmentId, ownerId, cancellationToken) is null) return false;
        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        await SetAsync(ownerId, commitmentId, new FollowUpQuiet(today.AddDays(days)), cancellationToken);
        return true;
    }

    public async Task<bool> DismissAsync(Guid ownerId, Guid commitmentId, CancellationToken cancellationToken)
    {
        if (await commitments.GetAsync(commitmentId, ownerId, cancellationToken) is null) return false;
        await SetAsync(ownerId, commitmentId, new FollowUpQuiet(null, true), cancellationToken);
        return true;
    }

    private async Task<FollowUpState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<FollowUpState>(ownerId, FollowUpState.Section, cancellationToken) ??
        new FollowUpState();

    private async Task SetAsync(Guid ownerId, Guid id, FollowUpQuiet quiet, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, FollowUpQuiet>((await LoadAsync(ownerId, cancellationToken)).Quiet ?? [])
        {
            [id.ToString()] = quiet
        };
        await settings.SaveAsync(ownerId, FollowUpState.Section, new FollowUpState(map), cancellationToken);
    }
}
