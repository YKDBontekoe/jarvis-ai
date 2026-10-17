using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Setup;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Setup;

internal sealed class SetupToolContributor(ISetupService setup, ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new SetupAgentTools(setup, currentUser);
        yield return AIFunctionFactory.Create(tools.GetSetupStatusAsync);
    }
}

/// <summary>Read-only: which setup steps are done, skipped or open, and what is not connected.</summary>
internal sealed class SetupAgentTools(ISetupService setup, ICurrentUser currentUser)
{
    [Description("Say how far the user's setup is: which first steps are done, skipped or still open, and which capabilities (calendar, morning brief, notifications) are connected. Use it when the user asks what is set up, what is not working, or what to do next. A step the user skipped is a choice: do not push it again. Only describe; changing anything happens in the app.")]
    public async Task<string> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        // The model is answering this question, so it is signed in.
        var view = await setup.GetAsync(currentUser.OwnerId, true, null, cancellationToken);
        var text = new StringBuilder(view.Summary).AppendLine();
        foreach (var goal in view.Goals)
            text.Append("- ").Append(goal.Title).Append(": ").AppendLine(goal.Done ? "done" : goal.Declined ? "skipped by choice" : "open");
        foreach (var capability in view.Capabilities.Where(c => c.Status != CapabilityStatuses.Ready))
            text.Append("Not ready: ").Append(capability.Title).Append(" (").Append(capability.Status).Append("), ").AppendLine(capability.Detail);
        return text.ToString();
    }
}
