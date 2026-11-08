using System.ComponentModel;
using Jarvis.Application.Conversations;
using Jarvis.Application.Preferences;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Preferences;

internal sealed class PreferenceToolContributor(IOwnerPreferenceService preferences, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new PreferenceAgentTools(preferences, currentUser);
        yield return AIFunctionFactory.Create(tools.SetHomeTimeZoneAsync);
    }
}

/// <summary>
/// Lets the owner say "I moved to Lisbon" in chat. It only changes how future times are read and shown; reminders that
/// are already scheduled keep their moment.
/// </summary>
internal sealed class PreferenceAgentTools(IOwnerPreferenceService preferences, ICurrentUser currentUser)
{
    [Description("Set the user's home time zone when they say where they live or that they moved or travel for good (\"I live in Lisbon now\"). Pass an IANA name such as Europe/Lisbon. Already scheduled reminders keep their moment. Do not call it for a trip or a one-off time conversion, and never guess a zone the user did not name.")]
    public async Task<string> SetHomeTimeZoneAsync(
        [Description("An IANA time zone such as Europe/Amsterdam or America/New_York.")] string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return "Which time zone? Ask the user for their city or time zone.";
        var result = await preferences.SetHomeZoneAsync(currentUser.OwnerId, timeZoneId.Trim(), cancellationToken);
        return result.Succeeded
            ? $"Home time zone is now {result.View!.HomeTimeZone}; it is {result.View.LocalNow} there. Reminders already scheduled keep their moment."
            : $"I could not set it: {result.Message}";
    }
}
