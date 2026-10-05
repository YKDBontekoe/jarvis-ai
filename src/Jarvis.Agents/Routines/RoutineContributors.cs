using Jarvis.Application.Conversations;
using Jarvis.Application.Routines;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Routines;

internal sealed class RoutineToolContributor(IRoutineSuggestionService routines, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new RoutineAgentTools(routines, currentUser);
        yield return AIFunctionFactory.Create(tools.GetRoutineSuggestionsAsync);
    }
}
