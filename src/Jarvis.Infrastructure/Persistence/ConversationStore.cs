using System.Text.Json;
using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ConversationStore(JarvisDbContext db) : IConversationStore, IConversationHistory,
    IConversationPreviews
{
    public async Task<IReadOnlyDictionary<Guid, ConversationPreview>> ListPreviewsAsync(Guid ownerId,
        IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0) return new Dictionary<Guid, ConversationPreview>();
        var ids = conversationIds.Distinct().ToArray();
        // One indexed lookup per listed conversation: newest message first, stopping at the first match. EF turns
        // "first message per group" into a window function that sorts the owner's whole history (content included,
        // spilling to disk) on every list refresh, so this is written as a lateral join over the (conversation,
        // created_at, id) index instead.
        var owned = await db.Database.SqlQuery<PreviewRow>($"""
            SELECT c."Id" AS "ConversationId", latest."Role" AS "Role", latest."Content" AS "Content",
                   latest."CreatedAt" AS "CreatedAt"
            FROM conversations AS c
            LEFT JOIN LATERAL (
                SELECT m."Role", m."Content", m."CreatedAt"
                FROM messages AS m
                WHERE m."ConversationId" = c."Id" AND m."Role" IN ('user', 'assistant')
                ORDER BY m."CreatedAt" DESC, m."Id" DESC
                LIMIT 1) AS latest ON TRUE
            WHERE c."OwnerId" = {ownerId} AND c."Id" = ANY({ids})
            """).ToListAsync(cancellationToken);
        var reminders = await db.Reminders.AsNoTracking()
            .Where(reminder => reminder.OwnerId == ownerId && reminder.ConversationId != null &&
                               ids.Contains(reminder.ConversationId.Value))
            .Select(reminder => new { ConversationId = reminder.ConversationId!.Value, reminder.Id })
            .ToListAsync(cancellationToken);
        var origins = reminders.GroupBy(item => item.ConversationId).ToDictionary(group => group.Key,
            group => group.First().Id);
        var result = new Dictionary<Guid, ConversationPreview>(owned.Count);
        foreach (var row in owned)
        {
            var hasOrigin = origins.TryGetValue(row.ConversationId, out var reminderId);
            result[row.ConversationId] = new ConversationPreview(row.ConversationId, row.Role,
                ConversationSnippets.Build(row.Content), row.CreatedAt,
                hasOrigin ? ConversationSnippets.OriginReminder : null, hasOrigin ? reminderId : null);
        }

        return result;
    }

    private sealed class PreviewRow
    {
        public Guid ConversationId { get; set; }
        public string? Role { get; set; }
        public string? Content { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    public async Task<IReadOnlyList<Message>> ListRecentMessagesAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken)
    {
        var conversations = await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.OwnerId == ownerId)
            .Select(conversation => new { conversation.Id, conversation.ProfileSnapshotJson })
            .ToListAsync(cancellationToken);
        var allowed = conversations
            .Where(conversation => ProfileScope.ContributesToLearning(conversation.ProfileSnapshotJson))
            .Select(conversation => conversation.Id)
            .ToHashSet();
        if (allowed.Count == 0) return [];

        return (await db.Messages.AsNoTracking()
            .Where(message => message.CreatedAt > since && allowed.Contains(message.ConversationId))
            .OrderByDescending(message => message.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken))
            .OrderBy(message => message.CreatedAt)
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, MessageLearningScope>> GetLearningScopesAsync(Guid ownerId,
        IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return new Dictionary<Guid, MessageLearningScope>();
        var rows = await (from message in db.Messages.AsNoTracking()
                join conversation in db.Conversations.AsNoTracking() on message.ConversationId equals conversation.Id
                where conversation.OwnerId == ownerId && messageIds.Contains(message.Id)
                select new { message.Id, conversation.ProfileId, conversation.ProfileSnapshotJson })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row =>
        {
            var snapshot = ProfileJson.Deserialize(row.ProfileSnapshotJson);
            return new MessageLearningScope(row.ProfileId, ProfileScope.AllowsRemember(snapshot),
                snapshot?.AllowPersonaLearning ?? true);
        });
    }

    public async Task<Conversation> CreateAsync(Guid ownerId, string title, CancellationToken cancellationToken,
        ProfileBinding? profile = null)
    {
        var conversation = new Conversation(ownerId, title);
        if (profile is not null)
            conversation.BindProfile(profile.ProfileId, profile.Version, profile.SnapshotJson);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task BindProfileAsync(Guid conversationId, Guid ownerId, ProfileBinding profile,
        CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(
            x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken)
            ?? throw new InvalidOperationException("Conversation was not found.");
        conversation.BindProfile(profile.ProfileId, profile.Version, profile.SnapshotJson);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<Conversation?> GetAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken) =>
        db.Conversations.SingleOrDefaultAsync(
            x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Conversations.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .Where(x => !db.Tasks.Any(task => task.ConversationId == x.Id))
            .OrderByDescending(x => x.PinnedAt != null).ThenByDescending(x => x.PinnedAt)
            .ThenByDescending(x => x.UpdatedAt).Take(100).ToListAsync(cancellationToken);

    public async Task<Conversation?> UpdateAsync(Guid conversationId, Guid ownerId, string? title, bool? pinned,
        CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(
            x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);
        if (conversation is null) return null;
        if (title is not null) conversation.Rename(title);
        if (pinned is { } pin) conversation.SetPinned(pin);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<ConversationDeleteResult> DeleteAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var conversation = await db.Conversations.SingleOrDefaultAsync(
            x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);
        if (conversation is null) return ConversationDeleteResult.NotFound;
        if (await db.Tasks.AnyAsync(x => x.ConversationId == conversationId, cancellationToken))
            return ConversationDeleteResult.TaskBacked;

        var messageIds = db.Messages.Where(x => x.ConversationId == conversationId).Select(x => x.Id);
        await db.Memories.Where(x => x.SourceType == "conversation" && x.SourceId != null &&
                messageIds.Contains(x.SourceId.Value))
            .ExecuteDeleteAsync(cancellationToken);
        var approvalIds = db.ToolApprovals.Where(x => x.ConversationId == conversationId).Select(x => x.Id);
        await db.Notifications.Where(x => x.SourceId != null && approvalIds.Contains(x.SourceId.Value))
            .ExecuteDeleteAsync(cancellationToken);
        db.Conversations.Remove(conversation);
        db.AuditEvents.Add(new AuditEvent(ownerId, "conversations", "conversation.deleted", "moderate", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = conversationId })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ConversationDeleteResult.Deleted;
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.Messages.AsNoTracking().Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task<MessagePage> GetMessagePageAsync(Guid conversationId, MessageCursor? before, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MessagePage.MaximumSize);

        var query = db.Messages.AsNoTracking().Where(message => message.ConversationId == conversationId);
        if (before is not null)
            query = query.Where(message => message.CreatedAt < before.CreatedAt ||
                message.CreatedAt == before.CreatedAt && message.Id.CompareTo(before.Id) < 0);

        var descending = await query.OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = descending.Count > limit;
        if (hasMore) descending.RemoveAt(descending.Count - 1);
        descending.Reverse();
        var nextCursor = hasMore && descending.Count > 0
            ? new MessageCursor(descending[0].CreatedAt, descending[0].Id)
            : null;
        return new MessagePage(descending, nextCursor, hasMore);
    }

    public async Task<IReadOnlyList<MessageSearchHit>> SearchMessagesAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return [];
        var pattern = "%" + trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        // The trigram index on messages.content drives the ILIKE; the owner join keeps every other owner's text out.
        var rows = await (
                from message in db.Messages.AsNoTracking()
                join conversation in db.Conversations.AsNoTracking() on message.ConversationId equals conversation.Id
                where conversation.OwnerId == ownerId &&
                      (message.Role == "user" || message.Role == "assistant") &&
                      EF.Functions.ILike(message.Content, pattern, "\\") &&
                      !db.Tasks.Any(task => task.ConversationId == conversation.Id)
                orderby message.CreatedAt descending, message.Id descending
                select new { message, conversation.Title })
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new MessageSearchHit(row.message, row.Title)).ToArray();
    }

    public async Task<MessageSearchHit?> FindMessageAsync(Guid ownerId, Guid messageId,
        CancellationToken cancellationToken)
    {
        var row = await (
                from message in db.Messages.AsNoTracking()
                join conversation in db.Conversations.AsNoTracking() on message.ConversationId equals conversation.Id
                where message.Id == messageId && conversation.OwnerId == ownerId
                select new { message, conversation.Title })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new MessageSearchHit(row.message, row.Title);
    }

    public async Task<MessageWindow?> GetMessageWindowAsync(Guid conversationId, Guid messageId, int before,
        int after, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(before, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(after, 0);
        var anchor = await db.Messages.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ConversationId == conversationId && x.Id == messageId, cancellationToken);
        if (anchor is null) return null;

        var older = await db.Messages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId &&
                        (x.CreatedAt < anchor.CreatedAt || x.CreatedAt == anchor.CreatedAt && x.Id.CompareTo(anchor.Id) < 0))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(before + 1).ToListAsync(cancellationToken);
        var hasOlder = older.Count > before;
        if (hasOlder) older.RemoveAt(older.Count - 1);
        older.Reverse();

        var newer = await db.Messages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId &&
                        (x.CreatedAt > anchor.CreatedAt || x.CreatedAt == anchor.CreatedAt && x.Id.CompareTo(anchor.Id) > 0))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(after + 1).ToListAsync(cancellationToken);
        var hasNewer = newer.Count > after;
        if (hasNewer) newer.RemoveAt(newer.Count - 1);

        var items = older.Append(anchor).Concat(newer).ToList();
        return new MessageWindow(items,
            hasOlder ? new MessageCursor(items[0].CreatedAt, items[0].Id) : null, hasOlder,
            hasNewer ? new MessageCursor(items[^1].CreatedAt, items[^1].Id) : null, hasNewer);
    }

    public async Task<MessageWindow> GetMessagesAfterAsync(Guid conversationId, MessageCursor after, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MessagePage.MaximumSize);
        var newer = await db.Messages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId &&
                        (x.CreatedAt > after.CreatedAt || x.CreatedAt == after.CreatedAt && x.Id.CompareTo(after.Id) > 0))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(limit + 1).ToListAsync(cancellationToken);
        var hasNewer = newer.Count > limit;
        if (hasNewer) newer.RemoveAt(newer.Count - 1);
        return new MessageWindow(newer, null, false,
            hasNewer && newer.Count > 0 ? new MessageCursor(newer[^1].CreatedAt, newer[^1].Id) : null, hasNewer);
    }

    public async Task AddMessageAsync(Message message, CancellationToken cancellationToken)
    {
        db.Messages.Add(message);
        var conversation = await db.Conversations.SingleAsync(x => x.Id == message.ConversationId, cancellationToken);
        if (message.Role == "user" && !await db.Messages.AnyAsync(
                x => x.ConversationId == message.ConversationId && x.Role == "user", cancellationToken))
            conversation.SetTitleFromFirstMessage(message.Content);
        conversation.Touch();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteMessageAsync(Guid conversationId, Guid messageId,
        CancellationToken cancellationToken) =>
        await db.Messages.Where(x => x.ConversationId == conversationId && x.Id == messageId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<string?> GetAgentSessionAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var state = await db.AgentSessions.Where(x => x.ConversationId == conversationId)
            .Select(x => x.State).SingleOrDefaultAsync(cancellationToken);
        return state is null ? null : AgentSessionJson.PrepareForRead(state);
    }

    public async Task SaveAgentSessionAsync(Guid conversationId, string state, CancellationToken cancellationToken)
    {
        var entry = await db.AgentSessions.SingleOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);
        if (entry is null)
            db.AgentSessions.Add(new AgentSessionState(conversationId, state));
        else
            entry.Replace(state);

        await db.SaveChangesAsync(cancellationToken);
    }
}
