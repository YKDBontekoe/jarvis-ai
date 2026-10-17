namespace Jarvis.Application.Meetings;

public static class MeetingTemplates
{
    public const string General = "general";
    public const string OneOnOne = "one_on_one";
    public const string ProjectUpdate = "project_update";
    public const string Interview = "interview";
    public const string Decision = "decision";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { General, OneOnOne, ProjectUpdate, Interview, Decision };
}

public static class MeetingRules
{
    public const int MaxTitleLength = 120;
    public const int MaxPurposeLength = 300;
    public const int MaxParticipants = 12;
    public const int MaxAgendaItems = 20;
    public const int MaxAgendaTextLength = 200;
    public const int MaxNotesLength = 4_000;
    public const int MaxDebriefLength = 8_000;
    public const int MaxCandidates = 30;
    public const int MaxPerOwner = 300;
}

public sealed record MeetingParticipant(string Name, Guid? PersonId = null);

/// <summary>One line of the agenda. A line that is not <see cref="Shared"/> stays out of a follow-up draft.</summary>
public sealed record AgendaItem(string Id, string Text, bool Shared = true);

public static class PrepItemKinds
{
    /// <summary>Something found in the owner's own records; always has a <see cref="PrepItem.Ref"/>.</summary>
    public const string Fact = "fact";

    /// <summary>Something the owner should answer: missing information turns into a question, never an invention.</summary>
    public const string Question = "question";
}

/// <summary>
/// One line of the prep pack. Facts carry a <c>type:id</c> reference to where they came from, so every reminder can be
/// opened and checked.
/// </summary>
public sealed record PrepItem(string Section, string Kind, string Text, string? Ref = null);

public sealed record MeetingPrep(DateTimeOffset BuiltAt, IReadOnlyList<PrepItem> Items);

public static class CandidateKinds
{
    public const string Decision = "decision";
    public const string IOwe = "i_owe";
    public const string TheyOwe = "they_owe";
    public const string Question = "question";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Decision, IOwe, TheyOwe, Question };
}

public static class CandidateStates
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Dismissed = "dismissed";
}

/// <summary>
/// Something a debrief seems to say. It is a proposal: nothing exists in Commitments or Decisions until the owner
/// accepts it. <see cref="Evidence"/> is the sentence it came from, shown as written; <see cref="DueText"/> is the date
/// wording when it could not be turned into a date.
/// </summary>
public sealed record DebriefCandidate(
    string Id,
    string Kind,
    string Text,
    string? Counterparty,
    DateOnly? DueOn,
    string? DueText,
    string Evidence,
    string State = CandidateStates.Pending,
    string? ResultRef = null);

public sealed record MeetingDocument(
    string? Purpose,
    string Template,
    IReadOnlyList<MeetingParticipant> Participants,
    IReadOnlyList<AgendaItem> Agenda,
    string? PrivateNotes,
    MeetingPrep? Prep,
    string? DebriefText,
    IReadOnlyList<DebriefCandidate> Candidates);

public sealed record MeetingRecord(
    Guid Id,
    Guid OwnerId,
    string Title,
    DateTimeOffset? StartsAt,
    Guid? ProjectId,
    MeetingDocument Document,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MeetingSummary(Guid Id, string Title, DateTimeOffset? StartsAt, Guid? ProjectId, int Open, int Accepted,
    DateTimeOffset UpdatedAt);

public sealed record MeetingInput(
    string? Title,
    DateTimeOffset? StartsAt,
    Guid? ProjectId,
    string? Purpose,
    string? Template,
    IReadOnlyList<MeetingParticipant>? Participants);

public sealed record MeetingEdit(
    string? Title,
    DateTimeOffset? StartsAt,
    string? Purpose,
    string? PrivateNotes,
    IReadOnlyList<AgendaItem>? Agenda);

/// <summary>What the owner chose for a candidate; blank fields keep what was found.</summary>
public sealed record CandidateAcceptance(
    string? Description = null,
    string? Counterparty = null,
    DateOnly? DueOn = null,
    bool ClearDue = false,
    double? Probability = null,
    DateOnly? ReviewOn = null);

public enum MeetingFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

public sealed record MeetingOperation<T>(T? Value, MeetingFailure Failure = MeetingFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == MeetingFailure.None;
    public static MeetingOperation<T> Ok(T value) => new(value);
    public static MeetingOperation<T> NotFound() => new(default, MeetingFailure.NotFound);
    public static MeetingOperation<T> Invalid(string field, string message) =>
        new(default, MeetingFailure.Invalid, field, message);
    public static MeetingOperation<T> Conflict(string message) => new(default, MeetingFailure.Conflict, null, message);
}

public sealed record FollowUpDraft(string Subject, string Body, bool Sent, IReadOnlyList<string> Included);

public sealed record MeetingCalendarEvent(string Title, DateTimeOffset StartAt, DateTimeOffset? EndAt);

public interface IMeetingRepository
{
    Task<IReadOnlyList<MeetingRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MeetingRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(MeetingRecord record, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(MeetingRecord record, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IMeetingService
{
    Task<IReadOnlyList<MeetingSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<MeetingRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<MeetingOperation<MeetingRecord>> CreateAsync(Guid ownerId, MeetingInput input,
        CancellationToken cancellationToken);
    Task<MeetingOperation<MeetingRecord>> UpdateAsync(Guid id, Guid ownerId, MeetingEdit edit,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Builds the prep pack from the owner's own records. Nothing is invented: what is missing becomes a question. The
    /// owner's agenda is only filled when it is empty.
    /// </summary>
    Task<MeetingOperation<MeetingRecord>> PrepareAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a debrief and lists what it seems to say as candidates. Running it again keeps the owner's earlier
    /// accept and dismiss choices.
    /// </summary>
    Task<MeetingOperation<MeetingRecord>> DebriefAsync(Guid id, Guid ownerId, string text,
        CancellationToken cancellationToken);

    /// <summary>Turns a candidate into a real commitment or decision. Accepting twice returns the same result.</summary>
    Task<MeetingOperation<MeetingRecord>> AcceptAsync(Guid id, Guid ownerId, string candidateId,
        CandidateAcceptance acceptance, CancellationToken cancellationToken);

    Task<MeetingOperation<MeetingRecord>> DismissAsync(Guid id, Guid ownerId, string candidateId,
        CancellationToken cancellationToken);

    /// <summary>A draft message built only from what the owner accepted and the agenda lines marked shared. Never sent.</summary>
    Task<MeetingOperation<FollowUpDraft>> FollowUpAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeetingCalendarEvent>> UpcomingEventsAsync(Guid ownerId, int days,
        CancellationToken cancellationToken);
}
