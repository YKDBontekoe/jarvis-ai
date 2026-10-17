using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Notifications;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Notifications;

internal sealed class NotificationToolContributor(INotificationPreferenceService preferences, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new NotificationAgentTools(preferences, currentUser);
        yield return AIFunctionFactory.Create(tools.ExplainNotificationsAsync);
    }
}

/// <summary>Read-only: why something did or did not buzz the phone. Changing it happens in the app.</summary>
internal sealed class NotificationAgentTools(INotificationPreferenceService preferences, ICurrentUser currentUser)
{
    [Description("Explain which kinds of notification reach the user's phone right now and why: their own per-kind choices plus the current mode (Sleep, Meeting, Focus…). Use it for \"why didn't I get a notification?\" or \"what will buzz me?\". Approvals and failures always buzz and always stay in the app. Only describe; changing a setting happens in the app.")]
    public async Task<string> ExplainNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var view = await preferences.GetAsync(currentUser.OwnerId, cancellationToken);
        var text = new StringBuilder(view.Summary).AppendLine();
        foreach (var category in view.Categories)
            text.Append("- ").Append(category.Title).Append(": ").AppendLine(category.Now.Reason);
        text.Append("Every notification stays in the app whether or not it buzzed the phone.");
        return text.ToString();
    }
}
