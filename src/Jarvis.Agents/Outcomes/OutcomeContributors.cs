using Jarvis.Application.Outcomes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Outcomes;

/// <summary>
/// Tells the agent the style preferences the owner approved from their own feedback (for example "keep project updates
/// short"). It is a style note only: it grants nothing, and it follows the profile in use, so another profile's
/// preferences do not leak in. Background tasks are not affected.
/// </summary>
internal sealed class OutcomeStyleContributor(IOutcomeLearningService outcomes) : IAgentContextContributor
{
    public int Order => 4;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        context.IsBackgroundTask ? [] : [new StyleProvider(outcomes, context.OwnerId, context.Profile?.ProfileId)];

    private sealed class StyleProvider(IOutcomeLearningService outcomes, Guid ownerId, Guid? profileId) : MessageAIContextProvider
    {
        protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            var note = await outcomes.StyleNoteAsync(ownerId, profileId, cancellationToken);
            return note is null ? [] : [new ChatMessage(ChatRole.User, note)];
        }
    }
}
