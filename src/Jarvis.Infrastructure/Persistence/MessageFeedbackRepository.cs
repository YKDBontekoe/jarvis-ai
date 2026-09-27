using Jarvis.Application.Persona;
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

    public async Task<IReadOnlyList<MessageFeedbackRecord>> ListUnprocessedAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken) =>
        await (from feedback in db.MessageFeedback.AsNoTracking()
                join message in db.Messages.AsNoTracking() on feedback.MessageId equals message.Id
                where feedback.OwnerId == ownerId && feedback.ProcessedAt == null
                orderby feedback.CreatedAt
                select new MessageFeedbackRecord(feedback.Id, feedback.ConversationId, feedback.MessageId,
                    feedback.Rating, feedback.Note,
                    message.Content.Length > 600 ? message.Content.Substring(0, 600) : message.Content,
                    feedback.CreatedAt))
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task MarkProcessedAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.MessageFeedback.Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessedAt, DateTimeOffset.UtcNow),
                cancellationToken);
}
