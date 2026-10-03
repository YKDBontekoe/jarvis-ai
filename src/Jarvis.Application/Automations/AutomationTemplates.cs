using Jarvis.Domain.Automations;

namespace Jarvis.Application.Automations;

public sealed record AutomationTemplate(
    string Id,
    string Title,
    string Description,
    string Category,
    Func<string, AutomationRuleDefinition> Build);

/// <summary>
/// Ready-made automations. Instantiating one creates a normal draft the owner reviews, edits and switches on; none
/// of them is active until then.
/// </summary>
public static class AutomationTemplates
{
    private static AutomationRuleDefinition Make(AutomationTriggerDefinition trigger,
        params AutomationActionDefinition[] actions) =>
        new(AutomationSchema.CurrentVersion, trigger, null, actions, null);

    public static readonly IReadOnlyList<AutomationTemplate> All =
    [
        new("morning-nudge", "Morning nudge", "A short notification at 08:00 to start the day.", "Daily",
            zone => Make(new ScheduleTriggerDefinition(new TimeOnly(8, 0), zone, null),
                new NotificationActionDefinition("Good morning", "Open Jarvis for your plan for today."))),
        new("weekday-prep", "Prepare my weekday", "On weekdays at 08:30 Jarvis prepares a plan for your day.", "Daily",
            zone => Make(new ScheduleTriggerDefinition(new TimeOnly(8, 30), zone, 0b0011111),
                new TaskActionDefinition("Prepare my day",
                    "Look at my calendar, reminders and open tasks and write a short plan for today."))),
        new("urgent-message", "Urgent message alert",
            "Notifies you when a chat you read along with contains the word \"urgent\".", "Messages",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.MessageReceived, "urgent", null),
                new NotificationActionDefinition("Urgent message", "{{event.title}}: {{event.detail}}"))),
        new("reply-needed", "Reply needed alert", "Notifies you when a conversation starts needing a reply.",
            "Messages",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.InboxNeedsReply, null, null),
                new NotificationActionDefinition("Needs a reply: {{event.title}}", "{{event.detail}}"))),
        new("file-summary", "Summarize uploaded files",
            "Starts an agent task that summarizes every file you upload. It asks for your approval each time.",
            "Files",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.FileUploaded, null, null),
                new AgentRunActionDefinition("Summarize {{event.title}}",
                    "Find the file named in the event and write a short summary of it with the key points."))),
        new("low-mood-checkin", "Gentle check-in after a rough day",
            "When a journal entry mentions a low mood, Jarvis sends a kind nudge.", "Wellbeing",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.JournalSaved, null, null),
                new NotificationActionDefinition("Rough day?", "Want to talk it through with Jarvis?")
                {
                    If = new AutomationActionCondition("event.detail", "contains", "mood 1/5")
                },
                new NotificationActionDefinition("Rough day?", "Want to talk it through with Jarvis?")
                {
                    If = new AutomationActionCondition("event.detail", "contains", "mood 2/5")
                })),
        new("webhook-to-task", "Webhook creates a task",
            "Every call to one of your webhooks becomes a task Jarvis works on.", "Integrations",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.Webhook, null, null),
                new TaskActionDefinition("Handle: {{event.title}}",
                    "A webhook was called. Decide what the user would want done about it and do it."))),
        new("task-finished-ping", "Task finished ping", "A notification whenever a task finishes.", "Tasks",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.TaskCompleted, null, null),
                new NotificationActionDefinition("Task done", "{{event.title}}"))),
        new("big-spend-watch", "Watch large expenses",
            "Notifies you when a logged expense mentions a category you want to keep an eye on.", "Money",
            _ => Make(new EventTriggerDefinition(AutomationEventKinds.ExpenseLogged, "shopping", null),
                new NotificationActionDefinition("Shopping expense", "{{event.title}}"))),
    ];

    public static AutomationTemplate? Find(string? id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
}
