using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Profiles;

internal sealed class ProfileGuidanceToolContributor(IProfileGuidanceService guidance, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new ProfileGuidanceTools(guidance, currentUser, context.Profile?.ProfileId);
        yield return AIFunctionFactory.Create(tools.SuggestWorkingStyleAsync);
    }
}

/// <summary>Read-only: suggests a working style (profile) for a task and explains what would change. It never switches.</summary>
internal sealed class ProfileGuidanceTools(IProfileGuidanceService guidance, ICurrentUser currentUser, Guid? currentProfileId)
{
    [Description("Suggest which assistant working style (profile) fits what the user is about to do, and say what would change in access if they switched. This only suggests; the user switches in the app, and a switch that widens access needs their confirmation.")]
    public async Task<string> SuggestWorkingStyleAsync(
        [Description("What the user wants to do, in their words.")] string task,
        CancellationToken cancellationToken = default)
    {
        var suggestion = await guidance.SuggestAsync(currentUser.OwnerId, task, currentProfileId, cancellationToken);
        if (suggestion.Kind == SuggestionKinds.None) return suggestion.Note;
        var text = new StringBuilder(suggestion.Reason).AppendLine();
        foreach (var detail in suggestion.ScopeChange?.Details ?? []) text.AppendLine("- " + detail);
        foreach (var wider in suggestion.Broadens) text.AppendLine("- Wider access: " + wider);
        text.Append(suggestion.Note);
        return text.ToString();
    }
}
