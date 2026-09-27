using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>Identifies the owner and run shape an agent is being built for.</summary>
public sealed record AgentBuildContext(Guid OwnerId, Guid? ExecutingTaskId)
{
    public bool IsBackgroundTask => ExecutingTaskId is not null;
}

/// <summary>Adds a feature's tools to the Jarvis agent. Implementations are scoped per agent run.</summary>
public interface IAgentToolContributor
{
    IEnumerable<AITool> GetTools(AgentBuildContext context);
}

/// <summary>Adds per-turn context (memory, persona, skills, and similar) to the Jarvis agent.</summary>
public interface IAgentContextContributor
{
    /// <summary>Lower values run first so stable context precedes turn-specific context.</summary>
    int Order { get; }

    IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context);
}
