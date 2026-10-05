namespace Jarvis.Domain.People;

/// <summary>
/// Ties a person on the owner's list to one of their one-to-one chats on a linked WhatsApp connection, which is
/// what lets the relationship radar see how the two of them keep in touch. <see cref="ChatId"/> is the chat's
/// canonical id (a phone number or an @lid). <see cref="ToneScore"/> (-2 cool to 2 warm) and
/// <see cref="ToneReason"/> are an optional model guess about recent messages, only filled when the owner turned
/// tone checks on; the messages themselves are never stored here.
/// </summary>
public sealed record PersonChannelLink(
    Guid Id,
    Guid OwnerId,
    Guid PersonId,
    Guid ConnectionId,
    string ChatId,
    int? ToneScore,
    string? ToneReason,
    DateTimeOffset? ToneAt,
    DateTimeOffset CreatedAt);
