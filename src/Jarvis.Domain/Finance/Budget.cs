namespace Jarvis.Domain.Finance;

/// <summary>
/// A monthly spending limit for one expense category, or for everything when <see cref="Category"/> is "total".
/// <see cref="AlertedMonth"/> (yyyy-MM) and <see cref="AlertedLevel"/> (0, 80 or 100) remember the last warning sent,
/// so crossing a threshold notifies once per month.
/// </summary>
public sealed record Budget(
    Guid Id,
    Guid OwnerId,
    string Category,
    decimal Limit,
    string Currency,
    string? AlertedMonth,
    int AlertedLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A charge that comes back on a schedule, found in the owner's expenses. <see cref="PreviousAmount"/> is set when
/// the latest charge differs from the one before. <see cref="RemindDaysBefore"/> asks for a reminder that many days
/// before <see cref="NextDueOn"/>; <see cref="ReminderId"/> is that reminder. <see cref="CancelUrl"/> is the page the
/// owner saved for cancelling. <see cref="NegotiationTaskId"/> is the background task that last drafted a cancel or
/// price message (<see cref="NegotiationGoal"/>, started at <see cref="NegotiationStartedAt"/>); doing it live in the
/// browser happens in a chat and leaves nothing here.
/// </summary>
public sealed record Subscription(
    Guid Id,
    Guid OwnerId,
    string MerchantKey,
    string Merchant,
    decimal Amount,
    string Currency,
    string Cadence,
    DateOnly LastChargedOn,
    DateOnly NextDueOn,
    int ChargeCount,
    decimal? PreviousAmount,
    string Status,
    int? RemindDaysBefore,
    Guid? ReminderId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CancelUrl = null,
    Guid? NegotiationTaskId = null,
    string? NegotiationGoal = null,
    DateTimeOffset? NegotiationStartedAt = null);
