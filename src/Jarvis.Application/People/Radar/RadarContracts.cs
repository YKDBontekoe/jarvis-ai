using Jarvis.Domain.People;

namespace Jarvis.Application.People.Radar;

public static class RadarRules
{
    public const int RecentDays = 30;
    public const int BaselineDays = 90;
    public const int HistoryDays = RecentDays + BaselineDays;
    public const int MaxLinksPerPerson = 5;
    public const int MaxLinksPerOwner = 200;
    public const int ToneMaxMessages = 40;
    public const int ToneMinMessages = 10;
    public const int ToneMaxPerRun = 10;
    public const int ToneRefreshDays = 7;
    public const int NudgeRepeatDays = 7;
    public const int MaxStatRows = 50_000;
    public const int MaxCandidates = 200;
    public const int MaxNudgeNames = 5;
    public const string NotificationType = "people.radar";
}

/// <summary>
/// The owner's radar choices and state, kept in owner settings. <see cref="ToneEnabled"/> is off until the owner
/// opts in because it sends a few recent messages of each linked chat to the model.
/// </summary>
public sealed record RadarSettings(bool ToneEnabled = false, DateTimeOffset? LastNudgedAt = null);

/// <summary>A message reduced to who sent it and when. No text is needed to see how people keep in touch.</summary>
public readonly record struct RadarMessage(bool FromMe, DateTimeOffset SentAt);

public readonly record struct ChatKey(Guid ConnectionId, string ChatId);

public sealed record ChatActivityStat(Guid ConnectionId, string ChatId, bool FromMe, DateTimeOffset SentAt);

/// <summary>Reads message counts and times without their text, newest history first.</summary>
public interface IChatActivityStats
{
    Task<IReadOnlyList<ChatActivityStat>> ListAsync(Guid ownerId, IReadOnlyCollection<ChatKey> chats,
        DateTimeOffset since, CancellationToken cancellationToken);
}

public static class RadarSignalKinds
{
    public const string Quiet = "quiet";
    public const string ReplySlower = "reply_slower";
    public const string YouInitiate = "you_initiate";
    public const string TheyInitiate = "they_initiate";
    public const string Unanswered = "unanswered";
    public const string Tone = "tone";
}

/// <summary>
/// One thing worth noticing. <see cref="Severity"/> is 1 for a note on the radar screen and 2 when it is worth a
/// notification.
/// </summary>
public sealed record RadarSignal(string Kind, int Severity, string Headline, string Detail);

/// <summary>How one person and the owner have been keeping in touch, from message times alone.</summary>
public sealed record RadarReport(
    Guid PersonId,
    string Name,
    DateTimeOffset? LastMessageAt,
    int RecentSent,
    int RecentReceived,
    double RecentPerWeek,
    double BaselinePerWeek,
    double? MyReplyMinutes,
    double? BaselineReplyMinutes,
    double? MyInitiationShare,
    double? BaselineInitiationShare,
    int UnansweredInbound,
    double? UnansweredHours,
    IReadOnlyList<int> WeeklyMessages,
    IReadOnlyList<RadarSignal> Signals,
    int? ToneScore,
    string? ToneReason)
{
    public int Severity => Signals.Count == 0 ? 0 : Signals.Max(x => x.Severity);
}

public sealed record RadarMessageText(bool FromMe, DateTimeOffset SentAt, string Text);

public sealed record RadarTone(int Warmth, string Reason);

/// <summary>
/// Guesses how recent messages with one person feel (-2 cool to 2 warm). The messages are untrusted data and the
/// answer is a guess the owner can ignore; it is only called when the owner turned tone checks on.
/// </summary>
public interface IRadarToneAnalyzer
{
    Task<RadarTone?> AnalyzeAsync(Guid ownerId, string personName, IReadOnlyList<RadarMessageText> messages,
        CancellationToken cancellationToken);
}

public interface IPersonLinkRepository
{
    Task<IReadOnlyList<PersonChannelLink>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<PersonChannelLink?> FindAsync(Guid ownerId, Guid connectionId, string chatId,
        CancellationToken cancellationToken);
    Task AddAsync(PersonChannelLink link, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid personId, Guid ownerId, CancellationToken cancellationToken);
    Task UpdateToneAsync(Guid id, Guid ownerId, int? score, string? reason, DateTimeOffset at,
        CancellationToken cancellationToken);
}

/// <summary>A link with the chat's name, so a screen can show who it is.</summary>
public sealed record PersonLinkView(Guid Id, Guid PersonId, Guid ConnectionId, string ChatId, string DisplayName,
    bool ReadAlong, DateTimeOffset CreatedAt);

/// <summary>A person and a chat that look like the same human, offered for the owner to confirm.</summary>
public sealed record LinkSuggestion(Guid PersonId, string PersonName, Guid ConnectionId, string ChatId,
    string ChatName);

/// <summary>A one-to-one chat the owner could link to a person, with whether Jarvis reads along with it.</summary>
public sealed record LinkCandidate(Guid ConnectionId, string ChatId, string DisplayName, bool ReadAlong);

public sealed record RadarOverview(bool ToneEnabled, IReadOnlyList<RadarReport> People);

public sealed record PersonRadarView(IReadOnlyList<PersonLinkView> Links, RadarReport? Report);

public sealed record RadarDailyResult(int Notified);

public interface IRelationshipRadarService
{
    /// <summary>The radar for every linked person, most worrying first. Also brings last-contact dates up to date.</summary>
    Task<RadarOverview> OverviewAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Null when the person does not exist.</summary>
    Task<PersonRadarView?> PersonAsync(Guid ownerId, Guid personId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonLinkView>?> LinksAsync(Guid ownerId, Guid personId, CancellationToken cancellationToken);

    Task<PeopleOperation<PersonLinkView>> LinkAsync(Guid ownerId, Guid personId, Guid connectionId, string chatId,
        CancellationToken cancellationToken);

    Task<bool> UnlinkAsync(Guid ownerId, Guid personId, Guid linkId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinkSuggestion>> SuggestAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Every one-to-one chat not linked to anyone yet, chats Jarvis reads along with first.</summary>
    Task<IReadOnlyList<LinkCandidate>> CandidatesAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<RadarSettings> SettingsAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<RadarSettings> SaveSettingsAsync(Guid ownerId, bool toneEnabled, CancellationToken cancellationToken);

    Task<bool> HasLinksAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Marks people as contacted when the owner has written to their linked chat since the last record.</summary>
    Task SyncLastContactAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>The daily pass: last-contact sync, optional tone refresh, and at most one weekly notification.</summary>
    Task<RadarDailyResult> DailyAsync(Guid ownerId, CancellationToken cancellationToken);
}
