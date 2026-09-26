using System.ComponentModel;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;

namespace Jarvis.Agents;

internal sealed class ReminderAgentTools(IReminderService reminders, ICurrentUser currentUser)
{
    [Description("Create a durable reminder that will notify the user at the requested time. Use an ISO 8601 timestamp with an explicit timezone offset. Ask a short clarification if the date or time is ambiguous.")]
    public async Task<string> CreateReminderAsync(
        [Description("A concise description of what the user should be reminded about.")] string title,
        [Description("The reminder time as an ISO 8601 date-time with a timezone offset.")] string dueAt,
        CancellationToken cancellationToken)
    {
        if (!DateTimeOffset.TryParse(dueAt, out var due))
            return "I could not schedule that reminder because its date and time were invalid.";

        try
        {
            var reminder = await reminders.CreateAsync(currentUser.OwnerId, title, due, cancellationToken);
            return $"Reminder scheduled for {reminder.DueAt:O}: {reminder.Title}";
        }
        catch (ArgumentException exception)
        {
            return $"I could not schedule that reminder: {exception.Message}";
        }
    }
}
