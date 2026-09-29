using System.Text;
using System.Text.Json;
using Jarvis.Application.Automations;

namespace Jarvis.Application.Conversations;

/// <summary>Assistant copy posted into reminder and automation chats. Titles are owner data, not instructions.</summary>
public static class LinkedConversationCopy
{
    public static string ReminderIntro(string title) =>
        $"This chat is linked to your reminder “{Sanitize(title)}”. I’ll post here when it fires. Reply to snooze, cancel, or talk about it.";

    public static string ReminderDue(string title) =>
        $"Reminder: {Sanitize(title)}\n\nThis is due now. Reply here to snooze, cancel, or talk about it.";

    public static string ReminderFailed(string title) =>
        $"I could not deliver your reminder “{Sanitize(title)}”. Reply here if you want me to reschedule it.";

    public static string AutomationIntro(string name) =>
        $"This chat is linked to your automation “{Sanitize(name)}”. I’ll post run results here. Reply to enable, disable, or change it.";

    public static string AutomationWaitingApproval(string name, string actionKind) =>
        $"Automation “{Sanitize(name)}” needs your approval to continue ({Sanitize(actionKind)}). Reply here after you decide, or ask me about the run.";

    public static string AutomationRun(string name, string status, string triggerReason, string? actionResultsJson,
        bool testRun = false)
    {
        var builder = new StringBuilder();
        if (testRun) builder.Append("Test run: ");
        builder.Append("Automation “").Append(Sanitize(name)).Append("” ").Append(status).Append('.');
        if (!string.IsNullOrWhiteSpace(triggerReason))
            builder.Append(" Trigger: ").Append(Sanitize(triggerReason)).Append('.');

        foreach (var result in ParseResults(actionResultsJson))
        {
            builder.Append("\n- ").Append(Sanitize(result.Kind)).Append(": ").Append(Sanitize(result.Status));
            if (!string.IsNullOrWhiteSpace(result.Detail))
                builder.Append(" — ").Append(Sanitize(result.Detail));
        }

        builder.Append("\n\nReply here to enable, disable, or change this automation.");
        return builder.ToString();
    }

    public static string Sanitize(string? value)
    {
        var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 200 ? text : text[..199] + "…";
    }

    private static IReadOnlyList<AutomationActionResult> ParseResults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<AutomationActionResult>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
