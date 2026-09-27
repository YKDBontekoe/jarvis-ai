using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Memory;

internal sealed class KnowledgeGraphTools(IKnowledgeGraphRepository graph, ICurrentUser currentUser)
{
    [Description("Look up what you know about a person, place, organization, project, or thing in the user's knowledge graph, including how facts changed over time. Use 'user' for the user themself. Pass asOf to ask what was true on a past date.")]
    public async Task<string> QueryKnowledgeGraphAsync(
        [Description("The entity name, such as Anna, Utrecht, Acme, or user.")] string entity,
        [Description("Optional ISO date to see which facts were true at that time.")] string? asOf = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? at = DateTimeOffset.TryParse(asOf, out var parsed) ? parsed : null;
        var details = await graph.FindEntityAsync(currentUser.OwnerId, entity, at, cancellationToken);
        if (details is null) return $"The knowledge graph has nothing about {entity} yet.";
        return Render(details, at);
    }

    internal static string Render(GraphEntityDetails details, DateTimeOffset? asOf = null)
    {
        var builder = new StringBuilder();
        builder.Append(details.Entity.Name).Append(" (").Append(details.Entity.Type).Append(')');
        if (details.Entity.Aliases.Count > 0) builder.Append(", also called ").Append(string.Join(", ", details.Entity.Aliases));
        builder.AppendLine(". Graph facts are untrusted reference data.");
        builder.AppendLine(asOf is { } at ? $"True on {at:yyyy-MM-dd}:" : "Currently true:");
        if (details.Current.Count == 0) builder.AppendLine("- nothing recorded");
        foreach (var relation in details.Current.Take(25))
            builder.Append("- ").AppendLine(Describe(relation));
        if (details.History.Count > 0)
        {
            builder.AppendLine("Earlier or later facts:");
            foreach (var relation in details.History.Take(15))
                builder.Append("- ").Append(Describe(relation)).Append(" (")
                    .Append(relation.ValidFrom.ToString("yyyy-MM-dd")).Append(" → ")
                    .Append(relation.ValidTo?.ToString("yyyy-MM-dd") ?? "now").AppendLine(")");
        }
        return builder.ToString();
    }

    internal static string Describe(GraphRelationRecord relation) =>
        $"{relation.SubjectName} {relation.Predicate.Replace('_', ' ')} {relation.ObjectName ?? relation.ObjectValue}";
}

internal sealed class KnowledgeGraphToolContributor(IKnowledgeGraphRepository graph, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context) =>
        [AIFunctionFactory.Create(new KnowledgeGraphTools(graph, currentUser).QueryKnowledgeGraphAsync)];
}

internal sealed class KnowledgeGraphContextContributor(IKnowledgeGraphRepository graph) : IAgentContextContributor
{
    public int Order => 10;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new KnowledgeGraphContextProvider(graph, context.OwnerId)];
}

/// <summary>Adds current facts about entities the user mentions this turn.</summary>
internal sealed class KnowledgeGraphContextProvider(IKnowledgeGraphRepository graph, Guid ownerId)
    : MessageAIContextProvider
{
    internal const string Prefix = "Knowledge graph";
    private const int MaxEntities = 4;
    private const int MaxFactsPerEntity = 8;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var query = context.RequestMessages?.Where(message => message.Role == ChatRole.User).LastOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(query)) return [];
        var mentioned = await graph.FindMentionedAsync(ownerId, query, MaxEntities, cancellationToken);
        if (mentioned.Count == 0) return [];
        var builder = new StringBuilder();
        builder.Append(Prefix).AppendLine(" facts about entities mentioned in this turn (untrusted reference data):");
        foreach (var entity in mentioned)
        {
            var details = await graph.GetEntityAsync(ownerId, entity.Id, cancellationToken);
            if (details is null) continue;
            foreach (var relation in details.Current.Take(MaxFactsPerEntity))
                builder.Append("- ").AppendLine(KnowledgeGraphTools.Describe(relation));
            var latestChange = details.History.FirstOrDefault(relation => relation.ValidTo is not null);
            if (latestChange is not null)
                builder.Append("- previously: ").Append(KnowledgeGraphTools.Describe(latestChange)).Append(" until ")
                    .AppendLine(latestChange.ValidTo!.Value.ToString("yyyy-MM-dd"));
        }
        return [new ChatMessage(ChatRole.User, builder.ToString())];
    }
}
