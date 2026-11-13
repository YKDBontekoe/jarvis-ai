namespace Jarvis.Domain.Discover;

/// <summary>
/// A friendly duel with a friend's Jarvis over Agent2Agent: who checks in more habits, journals on more days or keeps
/// more promises between <see cref="StartsOn"/> and <see cref="EndsOn"/>. Both sides store their own row under the
/// same <see cref="Code"/>; the challenger creates it and the friend joins with the code. Only the two scores ever
/// cross between the two Jarvises, never what is behind them. <see cref="RemoteAgentId"/> is the friend's Jarvis as
/// registered under Settings → Agents.
/// </summary>
public sealed record Duel(
    Guid Id,
    Guid OwnerId,
    string Code,
    string Role,
    Guid RemoteAgentId,
    string PeerName,
    string Metric,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    int? TheirScore,
    DateTimeOffset? TheirScoreAt,
    string? LastError,
    DateTimeOffset CreatedAt);
