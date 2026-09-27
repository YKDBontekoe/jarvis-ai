using System.Text.Json;
using Jarvis.Application.Conversations;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ConversationStore(JarvisDbContext db) : IConversationStore, IConversationHistory
{
    public async Task<IReadOnlyList<Message>> ListRecentMessagesAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken) =>
        (await db.Messages.AsNoTracking()
            .Where(message => message.CreatedAt > since &&
                              db.Conversations.Any(conversation => conversation.Id == message.ConversationId &&
                                                                   conversation.OwnerId == ownerId))
            .OrderByDescending(message => message.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken))
        .OrderBy(message => message.CreatedAt)
        .ToArray();

    public async Task<Conversation> CreateAsync(Guid ownerId, string title, CancellationToken cancellationToken)
    {
        var conversation = new Conversation(ownerId, title);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public Task<Conversation?> GetAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken) =>
        db.Conversations.SingleOrDefaultAsync(
            x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Conversations.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .Where(x => !db.Tasks.Any(task => task.ConversationId == x.Id))
            .OrderByDescending(x => x.UpdatedAt).Take(100).ToListAsync(cancellationToken);

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
