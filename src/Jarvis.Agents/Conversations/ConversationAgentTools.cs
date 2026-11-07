using System.ComponentModel;
using Jarvis.Application.Conversations;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Conversations;

internal sealed class ConversationToolContributor(IConversationStore conversations, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        // Only a chat has something to rename; background tasks run without one.
        if (context.ConversationId is not { } conversationId || context.IsBackgroundTask) yield break;
        var tools = new ConversationAgentTools(conversations, currentUser, conversationId);
        yield return AIFunctionFactory.Create(tools.RenameThisChatAsync);
        yield return AIFunctionFactory.Create(tools.PinThisChatAsync);
    }
}

/// <summary>Rename or pin the chat the owner is in. It only touches this one conversation, like the app's own menu.</summary>
internal sealed class ConversationAgentTools(IConversationStore conversations, ICurrentUser currentUser,
    Guid conversationId)
{
    public const int MaxTitleLength = 80;

    [Description("Rename this chat when the user asks for it (\"call this chat Trip planning\"). Use the title they gave; do not rename a chat on your own.")]
    public async Task<string> RenameThisChatAsync(
        [Description("The new title, a few words.")] string title,
        CancellationToken cancellationToken = default)
    {
        var clean = new string((title ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return "What should the chat be called?";
        if (clean.Length > MaxTitleLength) clean = clean[..MaxTitleLength].TrimEnd();
        var updated = await conversations.UpdateAsync(conversationId, currentUser.OwnerId, clean, null, cancellationToken);
        return updated is null ? "I could not find this chat." : $"This chat is now called \"{updated.Title}\".";
    }

    [Description("Pin this chat to the top of the chat list, or unpin it, when the user asks.")]
    public async Task<string> PinThisChatAsync(
        [Description("true to pin, false to unpin.")] bool pinned = true,
        CancellationToken cancellationToken = default)
    {
        var updated = await conversations.UpdateAsync(conversationId, currentUser.OwnerId, null, pinned, cancellationToken);
        return updated is null ? "I could not find this chat." : pinned ? "Pinned this chat." : "Unpinned this chat.";
    }
}
