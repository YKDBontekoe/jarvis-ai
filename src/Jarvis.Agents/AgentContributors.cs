using Jarvis.Application.Profiles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>Identifies the owner and run shape an agent is being built for.</summary>
public sealed record AgentBuildContext(
    Guid OwnerId,
    Guid? ExecutingTaskId,
    Guid? ConversationId = null,
    AssistantProfileSnapshot? Profile = null)
{
    public bool IsBackgroundTask => ExecutingTaskId is not null;
}

/// <summary>Adds a feature's tools to the Jarvis agent. Implementations are scoped per agent run.</summary>
public interface IAgentToolContributor
{
    IEnumerable<AITool> GetTools(AgentBuildContext context);
}

/// <summary>
/// Adds owner-authored working style (persona, profile) to the system instructions. Sections follow the shared
/// safety preamble and are framed as unable to override it.
/// </summary>
public interface IAgentInstructionContributor
{
    /// <summary>Lower values come first.</summary>
    int Order { get; }

    ValueTask<string?> GetInstructionsAsync(AgentBuildContext context, CancellationToken cancellationToken);
}

/// <summary>Adds per-turn context (memory, skills, and similar) to the Jarvis agent.</summary>
public interface IAgentContextContributor
{
    /// <summary>Lower values run first so stable context precedes turn-specific context.</summary>
    int Order { get; }

    IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context);
}
