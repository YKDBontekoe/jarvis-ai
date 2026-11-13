namespace Jarvis.Domain.Game;

/// <summary>
/// A bet Jarvis places against the owner: a claim about something the owner would rather not happen ("you will skip
/// Exercise on Thursday"). <see cref="Confidence"/> is how sure Jarvis is, from the owner's own history. The bet
/// settles itself from the owner's data between <see cref="StartsOn"/> and <see cref="EndsOn"/>: "won" means the
/// owner proved Jarvis wrong, "lost" means Jarvis was right, "void" means it was called off or no longer applies.
/// <see cref="SubjectKey"/> names what the bet is about, so the same thing is never bet on twice at once.
/// </summary>
public sealed record GameBet(
    Guid Id,
    Guid OwnerId,
    string Kind,
    string SubjectKey,
    Guid? SubjectId,
    string Claim,
    string Evidence,
    double Confidence,
    int Points,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsOpen => Status == "open";
}

/// <summary>
/// One of the week's side quests, written from the owner's own data ("Keep your promise to Rosa"). The boss is the
/// thing avoided longest. Some quests complete themselves when the data shows they are done; any can be ticked off
/// or skipped by hand.
/// </summary>
public sealed record GameQuest(
    Guid Id,
    Guid OwnerId,
    DateOnly WeekStart,
    string Kind,
    string SubjectKey,
    Guid? SubjectId,
    string Title,
    string Detail,
    int Target,
    int Points,
    bool IsBoss,
    string Status,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsOpen => Status == "open";
}
