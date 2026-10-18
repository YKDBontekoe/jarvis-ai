using Jarvis.Application.Preferences;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Preferences;

/// <summary>
/// Tells the agent the language the owner chose for replies. The active profile's own reply language wins, and a
/// message in another language is still answered in it, so this only sets the default.
/// </summary>
internal sealed class LocaleContextContributor(IOwnerLocaleService locale) : IAgentContextContributor
{
    public int Order => 5;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        context.IsBackgroundTask ? [] : [new LocaleProvider(locale, context.OwnerId, context.Profile?.ReplyLanguage)];

    private sealed class LocaleProvider(IOwnerLocaleService locale, Guid ownerId, string? profileLanguage) : MessageAIContextProvider
    {
        protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            var view = await locale.GetAsync(ownerId, cancellationToken);
            var note = LocaleRules.ReplyLanguageNote(profileLanguage, view.Language);
            return note is null ? [] : [new ChatMessage(ChatRole.User, note)];
        }
    }
}
