using Jarvis.Application.Conversations;
using Jarvis.Application.Decisions;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Decisions;

/// <summary>Decision tools work in every run; they only touch the owner's own decision journal.</summary>
internal sealed class DecisionToolContributor(IDecisionService decisions, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new DecisionAgentTools(decisions, currentUser);
        yield return AIFunctionFactory.Create(tools.GetDecisionsAsync);
        yield return AIFunctionFactory.Create(tools.GetCalibrationAsync);
        yield return AIFunctionFactory.Create(tools.LogDecisionAsync);
        yield return AIFunctionFactory.Create(tools.ResolveDecisionAsync);
    }
}
