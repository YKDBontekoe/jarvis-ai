namespace Jarvis.Application.Events;

/// <summary>Every kind of event the spine carries. Features publish these; handlers decide what follows.</summary>
public static class JarvisEventKinds
{
    public const string ReminderDue = "reminder.due";
    public const string WatchFired = "watch.fired";
    public const string TaskCompleted = "task.completed";
    public const string TaskFailed = "task.failed";
    public const string ApprovalRequested = "approval.requested";
    public const string ApprovalDecided = "approval.decided";
    public const string InboxNeedsReply = "inbox.needs_reply";
    public const string CommitmentCreated = "commitment.created";
    public const string JournalSaved = "journal.saved";
    public const string ExpenseLogged = "expense.logged";
    public const string FileUploaded = "file.uploaded";
    public const string MessageReceived = "message.received";
    public const string WebhookCalled = "webhook.called";
    public const string MemoryLearned = "memory.learned";
    public const string MissionStepCompleted = "mission.step_completed";
    public const string MissionStepFailed = "mission.step_failed";
    public const string HeartbeatCheckIn = "heartbeat.checkin";

    /// <summary>Jarvis did something on its own: an autonomous approval or a reaction that acted.</summary>
    public const string AgentActed = "agent.acted";

    public static readonly IReadOnlyList<string> All =
    [
        ReminderDue, WatchFired, TaskCompleted, TaskFailed, ApprovalRequested, ApprovalDecided, InboxNeedsReply,
        CommitmentCreated, JournalSaved, ExpenseLogged, FileUploaded, MessageReceived, WebhookCalled, MemoryLearned,
        MissionStepCompleted, MissionStepFailed, HeartbeatCheckIn, AgentActed
    ];

    public static string Describe(string kind) => kind switch
    {
        ReminderDue => "Reminder due",
        WatchFired => "Watch triggered",
        TaskCompleted => "Task finished",
        TaskFailed => "Task failed",
        ApprovalRequested => "Waiting for your approval",
        ApprovalDecided => "Approval decided",
        InboxNeedsReply => "Needs a reply",
        CommitmentCreated => "New commitment",
        JournalSaved => "Journal entry saved",
        ExpenseLogged => "Expense logged",
        FileUploaded => "File uploaded",
        MessageReceived => "Message received",
        WebhookCalled => "Webhook called",
        MemoryLearned => "Learned something",
        MissionStepCompleted => "Mission step done",
        MissionStepFailed => "Mission step failed",
        HeartbeatCheckIn => "Check-in",
        AgentActed => "Jarvis acted",
        _ => kind
    };
}

/// <summary>Who caused an event. Reactions never react to <see cref="AgentReaction"/> events, which stops loops.</summary>
public enum EventOrigin
{
    /// <summary>The owner did it, in the app or in chat.</summary>
    Owner,

    /// <summary>Jarvis did it during a turn or a background task.</summary>
    Agent,

    /// <summary>Jarvis did it while reacting to another event.</summary>
    AgentReaction,

    /// <summary>A schedule, a sync or an outside service did it.</summary>
    System
}

public static class JarvisEventLimits
{
    public const int MaxSummaryLength = 300;
    public const int MaxDataEntries = 12;
    public const int MaxDataValueLength = 400;
    public const int MaxListLimit = 200;
}

/// <summary>
/// Something that happened to one owner. <see cref="Summary"/> and <see cref="Data"/> can hold outside text (a message,
/// a file name), so they are data: inside prompts they are always marked as untrusted.
/// </summary>
public sealed record JarvisEvent(
    Guid OwnerId,
    string Kind,
    string Summary,
    EntityRef? Subject = null,
    IReadOnlyDictionary<string, string>? Data = null,
    Guid? ConversationId = null,
    EventOrigin Origin = EventOrigin.System,
    Guid? CausedByTaskId = null,
    DateTimeOffset? At = null,
    Guid? Id = null)
{
    public JarvisEvent Normalize(DateTimeOffset now) => this with
    {
        Id = Id is { } id && id != Guid.Empty ? id : Guid.CreateVersion7(),
        Kind = Kind.Trim().ToLowerInvariant(),
        Summary = Clip(Summary, JarvisEventLimits.MaxSummaryLength) ?? JarvisEventKinds.Describe(Kind),
        Data = Data is null
            ? null
            : Data.Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Take(JarvisEventLimits.MaxDataEntries)
                .ToDictionary(pair => Clip(pair.Key, 60)!, pair => Clip(pair.Value, JarvisEventLimits.MaxDataValueLength) ?? "",
                    StringComparer.Ordinal),
        At = At ?? now
    };

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }
}

/// <summary>Publishes an event to every handler. Owner scope comes from the event, which handlers must respect.</summary>
public interface IJarvisEventBus
{
    Task PublishAsync(JarvisEvent ev, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to events. Handlers run one after another in registration order; one failing never stops the others, and
/// never reaches the code that published the event.
/// </summary>
public interface IJarvisEventHandler
{
    bool Handles(JarvisEvent ev);
    Task HandleAsync(JarvisEvent ev, CancellationToken cancellationToken);
}

public static class JarvisEventPublishing
{
    /// <summary>
    /// Publishes if a bus is wired up and never lets a failure reach the caller: the reminder, upload or approval that
    /// raised the event matters more than anything that reacts to it.
    /// </summary>
    public static async Task TryPublishAsync(this IJarvisEventBus? bus, JarvisEvent ev,
        CancellationToken cancellationToken)
    {
        if (bus is null) return;
        try
        {
            await bus.PublishAsync(ev, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }
}

/// <summary>One stored event, as the activity feed and the agent read it back.</summary>
public sealed record OwnerEventRecord(
    Guid Id,
    Guid OwnerId,
    string Kind,
    string Summary,
    string? SubjectRef,
    IReadOnlyDictionary<string, string>? Data,
    Guid? ConversationId,
    EventOrigin Origin,
    Guid? CausedByTaskId,
    DateTimeOffset At);

public sealed record OwnerEventQuery(
    DateTimeOffset? Since = null,
    IReadOnlyCollection<string>? Kinds = null,
    IReadOnlyCollection<EventOrigin>? Origins = null,
    string? SubjectRef = null,
    int Limit = 50);

public interface IOwnerEventRepository
{
    Task AddAsync(JarvisEvent ev, CancellationToken cancellationToken);
    Task<IReadOnlyList<OwnerEventRecord>> ListAsync(Guid ownerId, OwnerEventQuery query, CancellationToken cancellationToken);
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}

/// <summary>Stores every event so the activity feed, the agent's situation and the related view can read it back.</summary>
public sealed class PersistingEventHandler(IOwnerEventRepository events) : IJarvisEventHandler
{
    public bool Handles(JarvisEvent ev) => true;

    public Task HandleAsync(JarvisEvent ev, CancellationToken cancellationToken) =>
        events.AddAsync(ev, cancellationToken);
}

/// <summary>Tells the owner's open apps that something happened, so feeds refresh without polling.</summary>
public sealed class RealtimeEventHandler(Realtime.IRealtimePublisher publisher) : IJarvisEventHandler
{
    public bool Handles(JarvisEvent ev) => true;

    public Task HandleAsync(JarvisEvent ev, CancellationToken cancellationToken) =>
        publisher.PublishToOwnerAsync(ev.OwnerId, "event.created", new
        {
            id = ev.Id,
            kind = ev.Kind,
            summary = ev.Summary,
            subject = ev.Subject?.ToString(),
            conversationId = ev.ConversationId,
            origin = ev.Origin.ToString(),
            at = ev.At
        }, cancellationToken);
}
