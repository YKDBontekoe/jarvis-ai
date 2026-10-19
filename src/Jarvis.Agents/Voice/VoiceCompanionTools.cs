using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Voice;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Voice;

internal sealed class VoiceCompanionToolContributor(IVoiceCompanionService companion, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        if (context.ConversationId is not { } conversationId || context.IsBackgroundTask) yield break;
        var tools = new VoiceCompanionTools(companion, currentUser, conversationId);
        yield return AIFunctionFactory.Create(tools.SaveShownCardAsync);
        yield return AIFunctionFactory.Create(tools.GetSessionRecapAsync);
    }
}

/// <summary>
/// For "save that" and "what did we do?" in a voice session. A card is saved only when it is clear which one the
/// owner means; otherwise the tool returns a short choice to ask about. Nothing here sends or changes anything else.
/// </summary>
internal sealed class VoiceCompanionTools(IVoiceCompanionService companion, ICurrentUser currentUser, Guid conversationId)
{
    [Description("Save the card that was just shown on the user's screen to their Library (\"save that\"). If two or more recent cards could be meant, this returns their names: ask the user which one and call again with part of its title. Never guess. Only saves; nothing is sent or changed elsewhere.")]
    public async Task<string> SaveShownCardAsync(
        [Description("Part of the card's title, only when the user said which one.")] string? which = null,
        CancellationToken cancellationToken = default)
    {
        var reference = await companion.ResolveLastAsync(currentUser.OwnerId, conversationId, which, cancellationToken);
        if (reference.Card is null)
            return reference.Choices.Count == 0
                ? "No card is waiting to be saved. Nothing was saved."
                : "More than one card could be meant: " + string.Join(" or ", reference.Choices.Select(c => $"\"{c.Title}\"")) +
                  ". Ask the user which one. Nothing was saved.";
        var result = await companion.SaveAsync(currentUser.OwnerId, reference.Card.Id, cancellationToken);
        return result.Succeeded
            ? $"Saved \"{result.Value!.Title}\" to the user's Library as a private note. Nothing else was sent or changed."
            : result.Message ?? "It could not be saved.";
    }

    [Description("Recap this voice session: how many cards were shown, which were saved, which were put away and which are still open. Use it when the user asks what you did or at the end of a session. It never claims anything was sent or accepted.")]
    public async Task<string> GetSessionRecapAsync(CancellationToken cancellationToken = default)
    {
        var recap = await companion.RecapAsync(currentUser.OwnerId, conversationId, cancellationToken);
        if (recap.Shown == 0) return "No cards were shown in this session. " + recap.Statement;
        var text = new StringBuilder($"{recap.Shown} shown, {recap.Saved} saved, {recap.Dismissed} put away, {recap.Open} still open.\n");
        foreach (var line in recap.Lines) text.Append("- ").Append(line.Title).Append(": ").AppendLine(line.Text);
        return text.Append(recap.Statement).ToString();
    }
}
