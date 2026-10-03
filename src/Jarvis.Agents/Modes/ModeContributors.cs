using Jarvis.Application.Conversations;
using Jarvis.Application.Modes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Modes;

internal sealed class ModeToolContributor(IModeService modes, ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new ModeAgentTools(modes, currentUser);
        yield return AIFunctionFactory.Create(tools.GetCurrentModeAsync);
        if (context.IsBackgroundTask) yield break;
        yield return AIFunctionFactory.Create(tools.SetModeAsync);
    }
}

/// <summary>
/// Tells the agent which mode the owner is in so its replies fit (short in Focus, calm in Sleep). The tone line is
/// the owner's own wording for that mode. Background tasks are not affected: a mode shapes conversation, not work.
/// </summary>
internal sealed class ModeContextContributor(IModeService modes) : IAgentContextContributor
{
    public int Order => 3;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        context.IsBackgroundTask ? [] : [new ModeContextProvider(modes, context.OwnerId)];

    private sealed class ModeContextProvider(IModeService modes, Guid ownerId) : MessageAIContextProvider
    {
        protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            var state = await modes.GetStateAsync(ownerId, cancellationToken);
            if (state.Current.Mode == ModeIds.Normal) return [];
            var definition = state.Modes.First(x => x.Definition.Id == state.Current.Mode).Definition;
            var tone = string.IsNullOrWhiteSpace(state.Policy.Tone) ? "" : " " + state.Policy.Tone;
            return [new ChatMessage(ChatRole.User,
                $"Context mode: the user is in {definition.Label} mode ({state.Current.Reason}).{tone} Switch modes with SetMode only when the user asks.")];
        }
    }
}
