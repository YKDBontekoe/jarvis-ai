using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.People;
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

    public static SearchRouteTarget Reminder(Guid reminderId, Guid? conversationId = null)
    {
        var parameters = new Dictionary<string, string> { ["reminderId"] = reminderId.ToString() };
        if (conversationId is Guid id) parameters["conversationId"] = id.ToString();
        return new("reminder", parameters);
    }

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

    public static SearchRouteTarget Message(Guid conversationId, Guid messageId) => new("message",
        new Dictionary<string, string>
        {
            ["conversationId"] = conversationId.ToString(),
            ["messageId"] = messageId.ToString()
        });

    public static SearchRouteTarget Person(Guid personId) => new("person",
        new Dictionary<string, string> { ["personId"] = personId.ToString() });

    public static SearchRouteTarget Commitment(Guid commitmentId) => new("commitment",
        new Dictionary<string, string> { ["commitmentId"] = commitmentId.ToString() });

    public static SearchRouteTarget Decision(Guid decisionId) => new("decision",
        new Dictionary<string, string> { ["decisionId"] = decisionId.ToString() });

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
        }
        return SearchRanking.Best(hits, limit);
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
        }
        return SearchRanking.Best(hits, limit);
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
                reminder.Place is { } place ? $"At {place.Name}" : $"Due {reminder.DueAt:u}", reminder.DueAt, SearchRoutes.Reminder(reminder.Id, reminder.ConversationId),
                SearchRanking.TextRelevance(reminder.Title, normalized), false));
        }
        return SearchRanking.Best(hits, limit);
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
        }
        return SearchRanking.Best(hits, limit);
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
            }
        }
        return SearchRanking.Best(hits, limit);
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
        }
        return SearchRanking.Best(hits, limit);
    }
}


/// <summary>People by name, relationship or notes. Notes are only matched, never shown in the snippet.</summary>
public sealed class PersonSearchProvider(IPeopleService people) : IFederatedSearchProvider
{
    public string ProviderId => "people";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Person };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var person in await people.ListAsync(ownerId, cancellationToken))
        {
            var relationship = person.Relationship ?? string.Empty;
            if (!SearchRanking.MatchesQuery(person.Name, normalized) &&
                !SearchRanking.MatchesQuery(relationship, normalized) &&
                !SearchRanking.MatchesQuery(person.Notes ?? string.Empty, normalized))
                continue;
            hits.Add(new FederatedSearchResult(SearchResultKinds.Person, person.Id.ToString(), person.Name,
                string.IsNullOrWhiteSpace(relationship) ? "Person" : relationship.Trim(),
                person.LastContactedAt ?? person.UpdatedAt, SearchRoutes.Person(person.Id),
                Math.Max(SearchRanking.TextRelevance(person.Name, normalized),
                    SearchRanking.TextRelevance(relationship, normalized) * .8 + .05), false));
        }
        return SearchRanking.Best(hits, limit);
    }
}

/// <summary>
/// Promises the owner made or was given, by what was promised or who it involves. Suggested ones are found too, and say
/// so, because they are not yet obligations.
/// </summary>
public sealed class CommitmentSearchProvider(ICommitmentService commitments) : IFederatedSearchProvider
{
    public string ProviderId => "commitments";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Commitment };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var commitment in await commitments.ListAsync(ownerId, null, null, true, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(commitment.Description, normalized) &&
                !SearchRanking.MatchesQuery(commitment.Counterparty, normalized))
                continue;
            var who = commitment.Direction == CommitmentDirections.IOwe
                ? $"You promised {commitment.Counterparty}"
                : $"{commitment.Counterparty} promised you";
            var state = commitment.Suggested ? "Suggested, not accepted yet"
                : commitment.Status != CommitmentStatuses.Open ? commitment.Status
                : commitment.DueOn is { } due ? $"Due {due:yyyy-MM-dd}" : "Open";
            hits.Add(new FederatedSearchResult(SearchResultKinds.Commitment, commitment.Id.ToString(),
                commitment.Description, $"{who} · {state}", commitment.UpdatedAt,
                SearchRoutes.Commitment(commitment.Id),
                Math.Max(SearchRanking.TextRelevance(commitment.Description, normalized),
                    SearchRanking.TextRelevance(commitment.Counterparty, normalized) * .7), false));
        }
        return SearchRanking.Best(hits, limit);
    }
}

/// <summary>Logged decisions by title, prediction or the background the owner wrote.</summary>
public sealed class DecisionSearchProvider(IDecisionService decisions) : IFederatedSearchProvider
{
    public string ProviderId => "decisions";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Decision };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var normalized = SearchRanking.NormalizeQuery(query);
        var hits = new List<FederatedSearchResult>();
        foreach (var decision in await decisions.ListAsync(ownerId, null, DecisionRules.MaxLimit, cancellationToken))
        {
            if (!SearchRanking.MatchesQuery(decision.Title, normalized) &&
                !SearchRanking.MatchesQuery(decision.Prediction, normalized) &&
                !SearchRanking.MatchesQuery(decision.Context ?? string.Empty, normalized))
                continue;
            var state = decision.Status switch
            {
                DecisionStatuses.Resolved => decision.Outcome == true ? "Happened" : "Did not happen",
                DecisionStatuses.Due => "Time to review",
                _ => $"Review {decision.ReviewOn:yyyy-MM-dd}"
            };
            hits.Add(new FederatedSearchResult(SearchResultKinds.Decision, decision.Id.ToString(), decision.Title,
                $"{SearchRanking.TruncateSummary(decision.Prediction, 120)} · {state}",
                decision.ResolvedAt ?? decision.CreatedAt, SearchRoutes.Decision(decision.Id),
                Math.Max(SearchRanking.TextRelevance(decision.Title, normalized),
                    SearchRanking.TextRelevance(decision.Prediction, normalized) * .8), false));
        }
        return SearchRanking.Best(hits, limit);
    }
}


/// <summary>
/// What was said, not just what a chat is called. Hits carry an excerpt around the match and open at that message.
/// Message hits are for the owner's own search screen: <c>SearchEverything</c> leaves them out, so a profile that is
/// restricted from the owner's other chats never reads them.
/// </summary>
public sealed class MessageSearchProvider(IConversationStore conversations) : IFederatedSearchProvider
{
    public const int ExcerptLength = 180;
    public string ProviderId => "messages";
    public IReadOnlySet<string> ResultKinds => new HashSet<string> { SearchResultKinds.Message };

    public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var hits = await conversations.SearchMessagesAsync(ownerId, query, limit, cancellationToken);
        return hits.Select(hit => new FederatedSearchResult(SearchResultKinds.Message, hit.Message.Id.ToString(),
            hit.ConversationTitle, (hit.Message.Role == "user" ? "You: " : "Jarvis: ") + Excerpt(hit.Message.Content, query),
            hit.Message.CreatedAt, SearchRoutes.Message(hit.Message.ConversationId, hit.Message.Id), .6, false)).ToArray();
    }

    /// <summary>A single line of text around the first match, so a long message or a code block stays readable.</summary>
    public static string Excerpt(string content, string query)
    {
        var flat = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var at = flat.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase);
        if (at < 0 || flat.Length <= ExcerptLength) return SearchRanking.TruncateSummary(flat, ExcerptLength);
        var start = Math.Max(0, at - ExcerptLength / 3);
        var end = Math.Min(flat.Length, start + ExcerptLength);
        start = Math.Max(0, end - ExcerptLength);
        return (start > 0 ? "…" : "") + flat[start..end].Trim() + (end < flat.Length ? "…" : "");
    }
}
