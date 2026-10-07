using Jarvis.Application.Conversations;
using Jarvis.Domain.Conversations;

namespace Jarvis.UnitTests;

/// <summary>A conversation history double: the given recent messages and, per message id, the profile rules.</summary>
internal static class TestHistory
{
    public static IConversationHistory Create(IReadOnlyList<Message>? messages = null,
        IReadOnlyDictionary<Guid, MessageLearningScope>? scopes = null) =>
        Fake<IConversationHistory>.Create(
            ("ListRecentMessagesAsync", _ => messages ?? []),
            ("GetLearningScopesAsync", args =>
            {
                var wanted = (IReadOnlyCollection<Guid>)args[1]!;
                return (IReadOnlyDictionary<Guid, MessageLearningScope>)(scopes ?? new Dictionary<Guid, MessageLearningScope>())
                    .Where(pair => wanted.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            }));
}
