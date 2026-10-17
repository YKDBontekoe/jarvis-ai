using Jarvis.Application.Inbox;
using Jarvis.Domain.Decisions;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Home;

/// <summary>An accepted promise that is due today or already late.</summary>
public sealed record HomeCommitmentDto(
    Guid Id,
    string Direction,
    string Counterparty,
    string Description,
    DateOnly DueOn,
    int DaysOverdue,
    string Source,
    Guid? InboxThreadId = null,
    Guid? ReminderId = null);

/// <summary>A decision whose review day has come and whose outcome the owner has not recorded.</summary>
public sealed record HomeDecisionDto(
    Guid Id,
    string Title,
    string Prediction,
    double Probability,
    DateOnly ReviewOn,
    int DaysOverdue,
    Guid? ReminderId = null);

/// <summary>
/// Picks the obligations that belong in Needs you. Pure, so every rule is tested with a fixed day. A suggested
/// commitment is only something Jarvis noticed; it becomes an obligation once the owner accepts it.
/// </summary>
public static class HomeAttention
{
    /// <summary>Per kind; the rest stay in their own screens rather than burying everything else on Home.</summary>
    public const int MaxPerKind = 5;

    public static IReadOnlyList<HomeCommitmentDto> Commitments(IEnumerable<Commitment> all, DateOnly today) =>
        all.Where(item => item is { Suggested: false, Status: CommitmentStatuses.Open, DueOn: not null } &&
                          item.DueOn <= today)
            // Late ones first, the longest overdue leading; then the day's own, what I owe before what is owed me.
            .OrderBy(item => item.DueOn)
            .ThenBy(item => item.Direction == CommitmentDirections.IOwe ? 0 : 1)
            .ThenBy(item => item.CreatedAt)
            .Take(MaxPerKind)
            .Select(item => new HomeCommitmentDto(item.Id, item.Direction, item.Counterparty, item.Description,
                item.DueOn!.Value, today.DayNumber - item.DueOn!.Value.DayNumber, item.Source, item.InboxThreadId,
                item.ReminderId))
            .ToArray();

    public static IReadOnlyList<HomeDecisionDto> Decisions(IEnumerable<Decision> all, DateOnly today) =>
        all.Where(item => !item.IsResolved && item.ReviewOn <= today)
            .OrderBy(item => item.ReviewOn)
            .ThenBy(item => item.CreatedAt)
            .Take(MaxPerKind)
            .Select(item => new HomeDecisionDto(item.Id, item.Title, item.Prediction, item.Probability,
                item.ReviewOn, today.DayNumber - item.ReviewOn.DayNumber, item.ReminderId))
            .ToArray();
}
