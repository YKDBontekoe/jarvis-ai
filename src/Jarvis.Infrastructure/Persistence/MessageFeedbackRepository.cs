using Jarvis.Application.Persona;
using Jarvis.Application.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MessageFeedbackEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public string Rating { get; set; } = "up";
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class MessageFeedbackRepository(JarvisDbContext db) : IMessageFeedbackRepository
{
    public async Task<MessageFeedbackRecord> SaveAsync(Guid ownerId, Guid conversationId, Guid messageId,
        string rating, string? note, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = await db.MessageFeedback.SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.MessageId == messageId, cancellationToken);
        if (entity is null)
        {
            entity = new MessageFeedbackEntity
            {
                Id = Guid.CreateVersion7(), OwnerId = ownerId, ConversationId = conversationId, MessageId = messageId
            };
            db.MessageFeedback.Add(entity);
        }
        entity.Rating = rating;
        entity.Note = note;
        entity.CreatedAt = now;
        entity.ProcessedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return new MessageFeedbackRecord(entity.Id, conversationId, messageId, rating, note, null, now);
    }

    /// <summary>
    /// Ratings reflection has not seen yet. A rating from a conversation whose assistant profile does not contribute to
    /// learning is never returned: its reply excerpt and note must not reach the model. Those rows are marked processed
    /// so they do not wait here forever, and the profile is read from the conversation, which cannot change afterwards.
    /// </summary>
    public async Task<IReadOnlyList<MessageFeedbackRecord>> ListUnprocessedAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken)
    {
        var rows = await (from feedback in db.MessageFeedback.AsNoTracking()
                join message in db.Messages.AsNoTracking() on feedback.MessageId equals message.Id
                join conversation in db.Conversations.AsNoTracking() on feedback.ConversationId equals conversation.Id
                where feedback.OwnerId == ownerId && feedback.ProcessedAt == null
                orderby feedback.CreatedAt
                select new
                {
                    Record = new MessageFeedbackRecord(feedback.Id, feedback.ConversationId, feedback.MessageId,
                        feedback.Rating, feedback.Note,
                        message.Content.Length > 600 ? message.Content.Substring(0, 600) : message.Content,
                        feedback.CreatedAt),
                    conversation.ProfileSnapshotJson
                })
            .Take(MaxScanned)
            .ToListAsync(cancellationToken);

        var excluded = rows.Where(row => !ProfileScope.ContributesToLearning(row.ProfileSnapshotJson))
            .Select(row => row.Record.Id).ToArray();
        if (excluded.Length > 0) await MarkProcessedAsync(ownerId, excluded, cancellationToken);
        var excludedSet = excluded.ToHashSet();
        return rows.Where(row => !excludedSet.Contains(row.Record.Id)).Select(row => row.Record).Take(limit).ToArray();
    }

    private const int MaxScanned = 500;

    public Task MarkProcessedAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.MessageFeedback.Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessedAt, DateTimeOffset.UtcNow),
                cancellationToken);
}
