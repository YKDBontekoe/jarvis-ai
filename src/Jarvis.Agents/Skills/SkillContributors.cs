using System.Text;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Skills;

internal sealed class SkillToolContributor(
    ISkillRepository skills,
    IOwnerSettingsStore settings,
    INotificationRepository notifications,
    IAuditEventStore audit,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new SkillAgentTools(skills, settings, notifications, audit, currentUser);
        yield return AIFunctionFactory.Create(tools.LoadSkillAsync);
        yield return AIFunctionFactory.Create(tools.ListSkillsAsync);
        yield return AIFunctionFactory.Create(tools.SaveSkillAsync);
    }
}

internal sealed class SkillContextContributor(ISkillRepository skills) : IAgentContextContributor
{
    public int Order => 20;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new SkillsContextProvider(skills, context.OwnerId)];
}

/// <summary>Progressive disclosure: only skill names and descriptions enter each turn; bodies load on demand.</summary>
internal sealed class SkillsContextProvider(ISkillRepository skills, Guid ownerId) : MessageAIContextProvider
{
    internal const string Prefix = "Available skills";
    private const int MaxListed = 40;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var active = (await skills.ListAsync(ownerId, cancellationToken))
            .Where(skill => skill.Status == SkillStatuses.Active)
            .OrderByDescending(skill => skill.UseCount).ThenByDescending(skill => skill.UpdatedAt)
            .Take(MaxListed)
            .ToArray();
        var builder = new StringBuilder();
        builder.Append(Prefix).AppendLine(" (your own saved procedures; call LoadSkill with the name before following one):");
        if (active.Length == 0) builder.AppendLine("- none yet");
        foreach (var skill in active)
            builder.Append("- ").Append(skill.Name).Append(": ").AppendLine(skill.Description);
        builder.Append("When you finish a non-trivial multi-step task you expect to repeat, or the user teaches you how they ")
            .Append("like something done, save or improve a skill with SaveSkill. Keep skills short and specific.");
        return [new ChatMessage(ChatRole.User, builder.ToString())];
    }
}
