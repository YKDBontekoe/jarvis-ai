using System.Text.RegularExpressions;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Inbox;

public static class InboxSources
{
    public const string WhatsApp = "whatsapp";
    public const string Mail = "mail";
    public const string Manual = "manual";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? source) =>
        source is WhatsApp or Mail or Manual;
}

public static class InboxStates
{
    public const string NeedsReply = "needs_reply";
    public const string Waiting = "waiting";
    public const string Fyi = "fyi";
    public const string Snoozed = "snoozed";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [NeedsReply, Waiting, Fyi, Snoozed, Done];

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? state) =>
        state is NeedsReply or Waiting or Fyi or Snoozed or Done;

    /// <summary>Display order: what needs the owner first, what is finished last.</summary>
    public static int Rank(string state) => state switch
    {
        NeedsReply => 0,
        Waiting => 1,
        Fyi => 2,
        Snoozed => 3,
        _ => 4
    };
}

public static class InboxPriorities
{
    public const int Low = 0;
    public const int Normal = 1;
    public const int High = 2;
    public const int Urgent = 3;

    public static int Clamp(int priority) => Math.Clamp(priority, Low, Urgent);
}

public static class CommitmentDirections
{
    public const string IOwe = "i_owe";
    public const string OwedToMe = "owed_to_me";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? direction) =>
        direction is IOwe or OwedToMe;
}

public static class CommitmentStatuses
{
    public const string Open = "open";
    public const string Done = "done";
    public const string Dropped = "dropped";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? status) =>
        status is Open or Done or Dropped;
}

public static class CommitmentSources
{
    public const string Chat = "chat";
    public const string WhatsApp = "whatsapp";
    public const string Mail = "mail";
    public const string Manual = "manual";
    public const string Meeting = "meeting";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? source) =>
        source is Chat or WhatsApp or Mail or Manual or Meeting;
}

public static class InboxRules
{
    public const int MaxTitleLength = 120;
    public const int MaxPreviewLength = 240;
    public const int MaxSummaryLength = 600;
    public const int MaxReplyLength = 1_500;
    public const int MaxCounterpartyLength = 80;
    public const int MaxDescriptionLength = 300;
    public const int MaxKeyLength = 200;
    public const int MaxThreads = 300;
    public const int ContextMessages = 20;
    public const int MaxSuggestionsPerTriage = 5;

    /// <summary>A snooze is a few hours to a few months; longer is probably a typo.</summary>
    public static readonly TimeSpan MaxSnooze = TimeSpan.FromDays(180);

    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? Limit(string? value, int max)
    {
        var clean = Clean(value);
        return clean is null ? null : clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Lowercase words without punctuation, used to spot a commitment that is already tracked.</summary>
    public static string Key(string? value) =>
        string.Join(' ', Regex.Split((value ?? string.Empty).ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(x => x.Length > 0));
}

/// <summary>A message in a thread, reduced to what triage needs.</summary>
public sealed record InboxMessageView(bool FromMe, string? Sender, string Text, DateTimeOffset At);

public sealed record CommitmentSuggestion(string Direction, string Counterparty, string Description, DateOnly? DueOn);

/// <summary>How a thread should be treated, as decided by rules or a model.</summary>
public sealed record InboxTriage(
    string State,
    int Priority,
    string? Summary,
    string? SuggestedReply,
    IReadOnlyList<CommitmentSuggestion> Commitments);

/// <summary>Decides whether a thread needs a reply and how urgent it is.</summary>
public interface IInboxTriager
{
    Task<InboxTriage> TriageAsync(Guid ownerId, InboxThread thread, IReadOnlyList<InboxMessageView> messages,
        DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>An item the agent found outside Jarvis (a mail thread, a to-do) and wants tracked in the inbox.</summary>
public sealed record ExternalInboxItem(
    string Source,
    string ExternalKey,
    string Title,
    string? Counterparty,
    string? Preview,
    bool FromMe,
    DateTimeOffset? At,
    bool? NeedsReply = null,
    int? Priority = null);

public sealed record InboxSyncResult(int Created, int Updated, int Scanned);

public sealed record InboxListing(IReadOnlyList<InboxThread> Threads, IReadOnlyDictionary<string, int> Counts);

public enum InboxFailure
{
    None,
    NotFound,
    Invalid,
    Unavailable
}

public sealed record InboxOperation<T>(T? Value, InboxFailure Failure = InboxFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == InboxFailure.None;

    public static InboxOperation<T> Ok(T value) => new(value);
    public static InboxOperation<T> NotFound() => new(default, InboxFailure.NotFound);
    public static InboxOperation<T> Invalid(string field, string message) =>
        new(default, InboxFailure.Invalid, field, message);
    public static InboxOperation<T> Unavailable(string message) =>
        new(default, InboxFailure.Unavailable, null, message);
}

public sealed record InboxTriageOutcome(InboxThread Thread, IReadOnlyList<Commitment> NewCommitments);

public interface IInboxRepository
{
    Task<IReadOnlyList<InboxThread>> ListThreadsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<InboxThread?> GetThreadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<InboxThread?> FindThreadAsync(Guid ownerId, string source, string externalKey,
        CancellationToken cancellationToken);
    Task AddThreadAsync(InboxThread thread, CancellationToken cancellationToken);
    Task<bool> UpdateThreadAsync(InboxThread thread, CancellationToken cancellationToken);
    Task<bool> DeleteThreadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Commitment>> ListCommitmentsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Commitment?> GetCommitmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddCommitmentAsync(Commitment commitment, CancellationToken cancellationToken);
    Task<bool> UpdateCommitmentAsync(Commitment commitment, CancellationToken cancellationToken);
    Task<bool> DeleteCommitmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IInboxService
{
    /// <summary>Pulls the chats the owner reads along with into the inbox and applies rule-based triage.</summary>
    Task<InboxSyncResult> SyncAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<InboxListing> ListAsync(Guid ownerId, IReadOnlySet<string>? states, CancellationToken cancellationToken);
    Task<InboxThread?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<InboxOperation<InboxThread>> SetStateAsync(Guid id, Guid ownerId, string state,
        CancellationToken cancellationToken);
    Task<InboxOperation<InboxThread>> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset until,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Runs the model triage on one thread: a summary, a draft reply, and suggested commitments.</summary>
    Task<InboxOperation<InboxTriageOutcome>> TriageAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Tracks something found outside Jarvis, for example a mail thread read through a connected app.</summary>
    Task<InboxOperation<InboxThread>> TrackAsync(Guid ownerId, ExternalInboxItem item,
        CancellationToken cancellationToken);
}

public sealed record CommitmentDraft(
    string? Direction,
    string? Counterparty,
    string? Description,
    DateOnly? DueOn = null,
    string Source = CommitmentSources.Manual,
    Guid? InboxThreadId = null);

public sealed record CommitmentSummary(int Open, int Overdue, int DueSoon, int IOwe, int OwedToMe, int Suggested);

public interface ICommitmentService
{
    Task<IReadOnlyList<Commitment>> ListAsync(Guid ownerId, string? direction, string? status, bool includeSuggested,
        CancellationToken cancellationToken);
    Task<Commitment?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Adds an accepted commitment and, when it has a due date, a reminder for that morning.</summary>
    Task<InboxOperation<Commitment>> CreateAsync(Guid ownerId, CommitmentDraft draft, DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>Stores commitments a model found. They stay suggestions until the owner accepts them.</summary>
    Task<IReadOnlyList<Commitment>> SuggestAsync(Guid ownerId, Guid? threadId, string source,
        IReadOnlyList<CommitmentSuggestion> suggestions, DateOnly today, CancellationToken cancellationToken);

    Task<InboxOperation<Commitment>> AcceptAsync(Guid id, Guid ownerId, DateOnly today,
        CancellationToken cancellationToken);
    Task<InboxOperation<Commitment>> SetStatusAsync(Guid id, Guid ownerId, string status,
        CancellationToken cancellationToken);
    Task<InboxOperation<Commitment>> UpdateAsync(Guid id, Guid ownerId, string? description, DateOnly? dueOn,
        bool clearDue, DateOnly today, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<CommitmentSummary> SummarizeAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken);
}
