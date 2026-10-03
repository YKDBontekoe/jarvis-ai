namespace Jarvis.Domain.Inbox;

/// <summary>
/// One conversation that may need the owner's attention, tracked across channels. <see cref="ExternalKey"/> is the
/// channel's own id for it (a WhatsApp chat, a mail thread), so syncing again updates the same row.
/// <see cref="Priority"/> runs from 0 (low) to 3 (urgent). <see cref="Summary"/> and <see cref="SuggestedReply"/>
/// are written by the model and are untrusted text.
/// </summary>
public sealed record InboxThread(
    Guid Id,
    Guid OwnerId,
    string Source,
    string ExternalKey,
    Guid? ConnectionId,
    string? ChatId,
    string Title,
    string? Counterparty,
    string State,
    int Priority,
    string? Summary,
    string? SuggestedReply,
    string? LastMessagePreview,
    bool LastFromMe,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? SnoozedUntil,
    DateTimeOffset? TriagedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Something promised: <c>i_owe</c> (the owner promised it) or <c>owed_to_me</c> (someone else did). A
/// <see cref="Suggested"/> commitment was found by Jarvis in a message and waits for the owner to accept it; only
/// accepted ones get a reminder. <see cref="ReminderId"/> is the reminder that fires on <see cref="DueOn"/>.
/// </summary>
public sealed record Commitment(
    Guid Id,
    Guid OwnerId,
    string Direction,
    string Counterparty,
    string Description,
    DateOnly? DueOn,
    string Status,
    bool Suggested,
    string Source,
    Guid? InboxThreadId,
    Guid? ReminderId,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
