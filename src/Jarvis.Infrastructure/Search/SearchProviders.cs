using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Application.Search;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;

namespace Jarvis.Infrastructure.Search;

internal static class SearchRoutes
{
    public static SearchRouteTarget Conversation(Guid conversationId) => new("conversation",
        new Dictionary<string, string> { ["conversationId"] = conversationId.ToString() });

    public static SearchRouteTarget Memory(Guid memoryId) => new("memory",
        new Dictionary<string, string> { ["memoryId"] = memoryId.ToString() });

    public static SearchRouteTarget File(Guid fileId) => new("file",
        new Dictionary<string, string> { ["fileId"] = fileId.ToString() });

    public static SearchRouteTarget Task(Guid taskId) => new("task",
        new Dictionary<string, string> { ["taskId"] = taskId.ToString() });

    public static SearchRouteTarget Reminder(Guid reminderId) => new("reminder",
        new Dictionary<string, string> { ["reminderId"] = reminderId.ToString() });

    public static SearchRouteTarget Skill(Guid skillId) => new("skill",
        new Dictionary<string, string> { ["skillId"] = skillId.ToString() });

    public static SearchRouteTarget GraphEntity(Guid entityId) => new("graph_entity",
        new Dictionary<string, string> { ["entityId"] = entityId.ToString() });

    public static SearchRouteTarget ChannelThread(Guid connectionId, string peer) => new("channel_thread",
        new Dictionary<string, string>
        {
            ["connectionId"] = connectionId.ToString(),
            ["peer"] = peer
        });

    public static SearchRouteTarget CodingRun(Guid runId) => new("coding_run",
        new Dictionary<string, string> { ["runId"] = runId.ToString() });
}

public sealed class ConversationSearchProvider(IConversationStore conversations) : IFederatedSearchProvider
{
    public string ProviderId => "conversations";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Conversation };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var conversation in await conversations.ListAsync(ownerId, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(conversation.Title, normalized)) continue;
            hits.Add(new FederatedSearchResult(SearchResultKinds.Conversation, conversation.Id.ToString(),
                conversation.Title, null, conversation.UpdatedAt, SearchRoutes.Conversation(conversation.Id),
                SearchRanking.TextRelevance(conversation.Title, normalized), false));
            if (hits.Count >= limit) break;
        }
        return hits;
    }
}

public sealed class MemorySearchProvider(IMemoryService memory) : IFederatedSearchProvider
{
    public string ProviderId => "memories";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Memory };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var hits = await memory.SearchAsync(ownerId, query, cancellationToken);
        return hits.Take(limit).Select(hit =>
        {
            var record = hit.Memory;
            var title = $"{record.Kind}: {SearchRanking.TruncateSummary(record.Content, 80)}";
            return new FederatedSearchResult(SearchResultKinds.Memory, record.Id.ToString(), title,
                SearchRanking.TruncateSummary(record.Content), record.UpdatedAt, SearchRoutes.Memory(record.Id),
                hit.Score, record.IsPinned);
        }).ToArray();
    }
}

public sealed class FileSearchProvider(IFileSearchService files) : IFederatedSearchProvider
{
    public string ProviderId => "files";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.File };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var hits = await files.SearchAsync(ownerId, query, cancellationToken);
        return hits.Take(limit).Select(hit => new FederatedSearchResult(SearchResultKinds.File,
            hit.FileId.ToString(), hit.FileName, SearchRanking.TruncateSummary(hit.Content), null,
            SearchRoutes.File(hit.FileId), hit.Score, false)).ToArray();
    }
}

public sealed class TaskSearchProvider(IJarvisTaskRepository tasks) : IFederatedSearchProvider
{
    public string ProviderId => "tasks";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Task };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var task in await tasks.ListAsync(ownerId, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(task.Title, normalized) &&
                !SearchRanking.MatchesQuery(task.Prompt, normalized) &&
                !SearchRanking.MatchesQuery(task.Summary ?? string.Empty, normalized))
                continue;
            hits.Add(new FederatedSearchResult(SearchResultKinds.Task, task.Id.ToString(), task.Title,
                SearchRanking.TruncateSummary(task.Summary ?? task.Prompt), task.CreatedAt,
                SearchRoutes.Task(task.Id), SearchRanking.TextRelevance(task.Title, normalized), false));
            if (hits.Count >= limit) break;
        }
        return hits;
    }
}

public sealed class ReminderSearchProvider(IReminderRepository reminders) : IFederatedSearchProvider
{
    public string ProviderId => "reminders";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Reminder };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var reminder in await reminders.ListRemindersAsync(ownerId, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(reminder.Title, normalized)) continue;
            hits.Add(new FederatedSearchResult(SearchResultKinds.Reminder, reminder.Id.ToString(), reminder.Title,
                $"Due {reminder.DueAt:u}", reminder.DueAt, SearchRoutes.Reminder(reminder.Id),
                SearchRanking.TextRelevance(reminder.Title, normalized), false));
            if (hits.Count >= limit) break;
        }
        return hits;
    }
}

public sealed class SkillSearchProvider(ISkillRepository skills) : IFederatedSearchProvider
{
    public string ProviderId => "skills";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Skill };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var skill in await skills.ListAsync(ownerId, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(skill.Name, normalized) &&
                !SearchRanking.MatchesQuery(skill.Description, normalized))
                continue;
            hits.Add(new FederatedSearchResult(SearchResultKinds.Skill, skill.Id.ToString(), skill.Name,
                SearchRanking.TruncateSummary(skill.Description), skill.UpdatedAt, SearchRoutes.Skill(skill.Id),
                SearchRanking.TextRelevance(skill.Name, normalized), false));
            if (hits.Count >= limit) break;
        }
        return hits;
    }
}

public sealed class GraphEntitySearchProvider(IKnowledgeGraphRepository graph) : IFederatedSearchProvider
{
    public string ProviderId => "graph";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.GraphEntity };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var entities = await graph.ListEntitiesAsync(ownerId, query, limit, cancellationToken);
        return entities.Select(entity => new FederatedSearchResult(SearchResultKinds.GraphEntity,
            entity.Id.ToString(), entity.Name, SearchRanking.TruncateSummary(entity.Summary), entity.UpdatedAt,
            SearchRoutes.GraphEntity(entity.Id), SearchRanking.TextRelevance(entity.Name, query), false)).ToArray();
    }
}

public sealed class ChannelThreadSearchProvider(IChannelRepository channels) : IFederatedSearchProvider
{
    public string ProviderId => "channels";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.ChannelThread };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var connection in await channels.ListAsync(ownerId, cancellationToken))
        {
            var threads = await channels.ListThreadsAsync(ownerId, connection.Id, cancellationToken);
            foreach (var thread in threads)
            {
                var label = $"{connection.DisplayName}: {thread.Peer}";
                var preview = thread.LastMessage?.Text;
                if (!SearchRanking.MatchesQuery(thread.Peer, normalized) &&
                    !SearchRanking.MatchesQuery(connection.DisplayName, normalized) &&
                    !SearchRanking.MatchesQuery(preview ?? string.Empty, normalized))
                    continue;
                hits.Add(new FederatedSearchResult(SearchResultKinds.ChannelThread,
                    $"{connection.Id}:{thread.Peer}", label, SearchRanking.TruncateSummary(preview),
                    thread.LastMessage?.CreatedAt, SearchRoutes.ChannelThread(connection.Id, thread.Peer),
                    SearchRanking.TextRelevance(label, normalized), false));
                if (hits.Count >= limit) return hits;
            }
        }
        return hits;
    }
}

public sealed class CodingRunSearchProvider(ICodingRunStore runs) : IFederatedSearchProvider
{
    public string ProviderId => "coding";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.CodingRun };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var run in await runs.ListAsync(ownerId, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(run.Repository, normalized) &&
                !SearchRanking.MatchesQuery(run.Task, normalized) &&
                !SearchRanking.MatchesQuery(run.Summary ?? string.Empty, normalized))
                continue;
            hits.Add(new FederatedSearchResult(SearchResultKinds.CodingRun, run.Id.ToString(),
                $"{run.Repository}: {SearchRanking.TruncateSummary(run.Task, 80)}",
                SearchRanking.TruncateSummary(run.Summary ?? run.DiffSummary), run.CreatedAt,
                SearchRoutes.CodingRun(run.Id), SearchRanking.TextRelevance(run.Task, normalized), false));
            if (hits.Count >= limit) break;
        }
        return hits;
    }
}
