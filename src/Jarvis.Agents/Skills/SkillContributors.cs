using System.Text;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;
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
    ICurrentUser currentUser,
    Jarvis.Application.Learning.ITurnTraceCollector? trace = null,
    AgentToolCatalog? catalog = null) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new SkillAgentTools(skills, settings, notifications, audit, currentUser, context.Profile, trace,
            catalog);
        yield return AIFunctionFactory.Create(tools.LoadSkillAsync);
        yield return AIFunctionFactory.Create(tools.ListSkillsAsync);
        yield return AIFunctionFactory.Create(tools.SaveSkillAsync);
        yield return AIFunctionFactory.Create(tools.RetireSkillAsync);
    }
}

internal sealed class SkillContextContributor(ISkillRepository skills, AgentToolCatalog? catalog = null)
    : IAgentContextContributor
{
    public int Order => 20;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new SkillsContextProvider(skills, context.OwnerId, context.Profile, catalog)];
}

/// <summary>Progressive disclosure: only skill names and descriptions enter each turn; bodies load on demand.</summary>
internal sealed class SkillsContextProvider(ISkillRepository skills, Guid ownerId,
    AssistantProfileSnapshot? profile, AgentToolCatalog? catalog = null) : MessageAIContextProvider
{
    internal const string Prefix = "Available skills";
    private const int MaxListed = 40;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var active = (await skills.ListAsync(ownerId, cancellationToken))
            .Where(skill => skill.Status == SkillStatuses.Active)
            .Where(skill => ProfileScope.AllowsSkill(profile, skill.Id))
            .OrderByDescending(SkillOutcomeRules.Score).ThenByDescending(skill => skill.UseCount)
            .ThenByDescending(skill => skill.UpdatedAt)
            .Take(MaxListed)
            .ToArray();
        var builder = new StringBuilder();
        builder.Append(Prefix).AppendLine(" (your own saved procedures; call LoadSkill with the name before following one):");
        if (active.Length == 0) builder.AppendLine("- none yet");
        foreach (var skill in active)
        {
            builder.Append("- ").Append(skill.Name).Append(": ").Append(skill.Description);
            var missing = catalog?.Missing(skill.RequiredTools) ?? [];
            if (missing.Count > 0) builder.Append(" (unavailable now: needs ").Append(string.Join(", ", missing)).Append(')');
            builder.AppendLine();
        }
        builder.Append("When you finish a non-trivial multi-step task you expect to repeat, or the user teaches you how they ")
            .Append("like something done, save or improve a skill with SaveSkill, listing in requiredTools the app tools it ")
            .Append("calls. Keep skills short and specific. If a request needs a capability none of your tools has, call ")
            .Append("ReportCapabilityGap before answering.");
        return [new ChatMessage(ChatRole.User, builder.ToString())];
    }
}
