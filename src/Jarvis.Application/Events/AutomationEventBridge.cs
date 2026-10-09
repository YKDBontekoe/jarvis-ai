using Jarvis.Application.Automations;

namespace Jarvis.Application.Events;

/// <summary>
/// Sends every automation event on to the event spine as well, so the places that already announce uploads,
/// journal entries, expenses, finished tasks, read-along messages, inbox replies and webhooks feed the activity
/// feed and Jarvis's reactions without each having to publish twice. Automations still run first and still
/// decide the returned count.
/// </summary>
public sealed class AutomationEventBridge(IAutomationEventBus automations, IJarvisEventBus spine) : IAutomationEventBus
{
    public async Task<int> PublishAsync(Guid ownerId, AutomationEvent ev, CancellationToken cancellationToken)
    {
        var started = await automations.PublishAsync(ownerId, ev, cancellationToken);
        if (ToSpine(ownerId, ev) is { } spineEvent) await spine.TryPublishAsync(spineEvent, cancellationToken);
        return started;
    }

    public static JarvisEvent? ToSpine(Guid ownerId, AutomationEvent ev)
    {
        ev = ev.Normalize();
        var (kind, subjectType, origin) = ev.Kind switch
        {
            AutomationEventKinds.TaskCompleted => (JarvisEventKinds.TaskCompleted, EntityTypes.Task, EventOrigin.Agent),
            AutomationEventKinds.FileUploaded => (JarvisEventKinds.FileUploaded, EntityTypes.File, EventOrigin.Owner),
            AutomationEventKinds.JournalSaved => (JarvisEventKinds.JournalSaved, EntityTypes.Journal, EventOrigin.Owner),
            AutomationEventKinds.ExpenseLogged => (JarvisEventKinds.ExpenseLogged, EntityTypes.Expense, EventOrigin.Owner),
            AutomationEventKinds.InboxNeedsReply => (JarvisEventKinds.InboxNeedsReply, (string?)null, EventOrigin.System),
            AutomationEventKinds.MessageReceived => (JarvisEventKinds.MessageReceived, null, EventOrigin.System),
            AutomationEventKinds.Webhook => (JarvisEventKinds.WebhookCalled, null, EventOrigin.System),
            _ => (null, null, EventOrigin.System)
        };
        if (kind is null) return null;
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { ["title"] = ev.Title };
        if (ev.Detail is { } detail) data["detail"] = detail;
        if (ev.Source is { } source) data["source"] = source;
        return new JarvisEvent(ownerId, kind, ev.Title,
            subjectType is not null && ev.Ref is { } id && id != Guid.Empty ? new EntityRef(subjectType, id) : null,
            data, Origin: origin, At: ev.At);
    }
}
