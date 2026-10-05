using Jarvis.Application.Channels;
using Jarvis.Application.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class WhatsAppChatEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConnectionId { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public bool ReadAlong { get; set; }
    public bool AutoReminders { get; set; }
    /// <summary>When the newest stored message was sent (for sorting and display).</summary>
    public DateTimeOffset? LastMessageAt { get; set; }
    /// <summary>When Jarvis last stored a message for this chat; the reminder scan compares against this.</summary>
    public DateTimeOffset? LastReceivedAt { get; set; }
    /// <summary>Creation time of the last message viewed in Jarvis, independent of WhatsApp read receipts.</summary>
    public DateTimeOffset? ReadThrough { get; set; }
    /// <summary>Messages stored after this moment have not been scanned for reminders yet.</summary>
    public DateTimeOffset? ScannedThrough { get; set; }
    public DateTimeOffset? ScanLeaseUntil { get; set; }
    /// <summary>The Jarvis conversation used for "Ask Jarvis" about this chat.</summary>
    public Guid? AskConversationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public WhatsAppChatSettings ToRecord() => new(Id, OwnerId, ConnectionId, ChatId, DisplayName, IsGroup, ReadAlong,
        AutoReminders, LastMessageAt, CreatedAt, UpdatedAt, AskConversationId);
}

public sealed class WhatsAppMessageEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConnectionId { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public bool FromMe { get; set; }
    public string? Sender { get; set; }
    /// <summary>Phone number or @lid of the person who sent a group message, used to load their picture.</summary>
    public string? SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>JSON description of a photo, sticker or other WhatsApp element, without the bytes.</summary>
    public string? MediaJson { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public WhatsAppChatMessage ToRecord()
    {
        var (media, quote) = WhatsAppMediaCodec.Read(MediaJson);
        return new(Id, ConnectionId, ChatId, ExternalId, FromMe, Sender, Text, SentAt, CreatedAt, SenderId, media, quote);
    }
}

public sealed class WhatsAppMessageMediaEntity
{
    public Guid MessageId { get; set; }
    public Guid OwnerId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}

public sealed class WhatsAppAssistantRepository(JarvisDbContext db, TimeProvider? clock = null)
    : IWhatsAppAssistantRepository
{
    private static readonly TimeSpan ScanLease = TimeSpan.FromMinutes(5);
    private const int MaxNewMessagesPerScan = 40;
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<bool> MergeChatAliasesAsync(Guid ownerId, Guid connectionId, string phoneId,
        IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        if (WhatsAppChatIds.Normalize(phoneId) != phoneId || !phoneId.StartsWith('+')) return false;
        var lids = aliases.Where(id => WhatsAppChatIds.Normalize(id) == id &&
            id.EndsWith("@lid", StringComparison.Ordinal)).Distinct().ToArray();
        if (lids.Length == 0) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockConnectionAsync(connectionId, cancellationToken);
        if (!await db.ChannelConnections.AnyAsync(c => c.Id == connectionId && c.OwnerId == ownerId &&
                c.Kind == ChannelKinds.WhatsAppLinked, cancellationToken)) return false;
        var ids = lids.Append(phoneId).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM whatsapp_chats
            WHERE owner_id = {ownerId} AND connection_id = {connectionId} AND chat_id = ANY({ids})
            ORDER BY "Id" FOR UPDATE
            """, cancellationToken);
        var rows = await db.WhatsAppChats.Where(c => c.OwnerId == ownerId && c.ConnectionId == connectionId &&
            ids.Contains(c.ChatId)).ToListAsync(cancellationToken);
        if (!rows.Any(c => lids.Contains(c.ChatId))) return false;
        // Do not move a chat while its reminder scan is using the old identity.
        if (rows.Any(c => c.ScanLeaseUntil > time.GetUtcNow())) return false;
        var target = rows.FirstOrDefault(c => c.ChatId == phoneId) ?? rows.OrderBy(c => c.CreatedAt).First();
        // Honor the owner's latest choice, including turning reading/reminders off on either identity.
        var choice = rows.OrderByDescending(c => c.UpdatedAt).First();
        target.ReadAlong = choice.ReadAlong;
        target.AutoReminders = choice.AutoReminders;
        var withMessages = await db.WhatsAppMessages.Where(m => m.OwnerId == ownerId &&
            m.ConnectionId == connectionId && ids.Contains(m.ChatId)).Select(m => m.ChatId)
            .Distinct().ToArrayAsync(cancellationToken);
        var history = rows.Where(c => withMessages.Contains(c.ChatId)).ToArray();
        if (history.Length == 0) history = rows.ToArray();
        // An empty duplicate must not make an already read conversation unread again.
        target.ReadThrough = history.Any(c => c.ReadThrough is null) ? null : history.Min(c => c.ReadThrough);
        target.ScannedThrough = history.Max(c => c.ScannedThrough);
        target.LastMessageAt = rows.Max(c => c.LastMessageAt);
        target.LastReceivedAt = rows.Max(c => c.LastReceivedAt);
        target.CreatedAt = rows.Min(c => c.CreatedAt);
        target.UpdatedAt = rows.Max(c => c.UpdatedAt);
        target.AskConversationId ??= rows.Select(c => c.AskConversationId).FirstOrDefault(id => id is not null);
        if (target.DisplayName == WhatsAppChatIds.FallbackName(target.ChatId))
            target.DisplayName = rows.FirstOrDefault(c => c.DisplayName != WhatsAppChatIds.FallbackName(c.ChatId))
                ?.DisplayName ?? phoneId;
        target.ChatId = phoneId;
        await db.WhatsAppMessages.Where(m => m.OwnerId == ownerId && m.ConnectionId == connectionId &&
            lids.Contains(m.ChatId)).ExecuteUpdateAsync(s => s.SetProperty(m => m.ChatId, phoneId), cancellationToken);
        db.WhatsAppChats.RemoveRange(rows.Where(c => c != target));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Serialize consolidation with setting changes and arrivals so a queued message keeps its history.
    private Task<int> LockConnectionAsync(Guid connectionId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM channel_connections WHERE "Id" = {connectionId} FOR UPDATE
            """, cancellationToken);

    public async Task<IReadOnlyList<WhatsAppChatSettings>> ListChatsAsync(Guid ownerId, Guid? connectionId,
        CancellationToken cancellationToken) =>
        (await db.WhatsAppChats.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && (connectionId == null || x.ConnectionId == connectionId))
            .OrderByDescending(x => x.LastMessageAt).ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<WhatsAppChatSettings?> GetChatAsync(Guid ownerId, Guid connectionId, string chatId,
        CancellationToken cancellationToken) =>
        (await db.WhatsAppChats.AsNoTracking().SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.ConnectionId == connectionId && x.ChatId == chatId,
            cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<WhatsAppChatActivity>> ListActivityAsync(Guid ownerId, Guid connectionId,
        CancellationToken cancellationToken) =>
        await (from chat in db.WhatsAppChats.AsNoTracking()
               where chat.OwnerId == ownerId && chat.ConnectionId == connectionId && chat.ReadAlong
               let latest = db.WhatsAppMessages.Where(m => m.OwnerId == ownerId &&
                   m.ConnectionId == connectionId && m.ChatId == chat.ChatId)
                   .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).FirstOrDefault()
               select new WhatsAppChatActivity(chat.ChatId,
                   latest == null ? null : latest.Text.Substring(0, Math.Min(latest.Text.Length, 160)),
                   latest == null ? null : (bool?)latest.FromMe,
                   db.WhatsAppMessages.Count(m => m.OwnerId == ownerId && m.ConnectionId == connectionId &&
                       m.ChatId == chat.ChatId && !m.FromMe && (chat.ReadThrough == null || m.CreatedAt > chat.ReadThrough))))
            .ToListAsync(cancellationToken);

    public async Task<bool> MarkReadAsync(Guid ownerId, Guid connectionId, string chatId, Guid messageId,
        CancellationToken cancellationToken)
    {
        var message = await db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId &&
            m.OwnerId == ownerId && m.ConnectionId == connectionId && m.ChatId == chatId, cancellationToken);
        if (message is null) return false;
        // A stale tab must not move the watermark backwards.
        await db.WhatsAppChats.Where(c => c.OwnerId == ownerId && c.ConnectionId == connectionId &&
            c.ChatId == chatId && (c.ReadThrough == null || c.ReadThrough < message.CreatedAt))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReadThrough, message.CreatedAt), cancellationToken);
        return true;
    }

    public async Task<WhatsAppChatSettings?> SaveChatAsync(Guid ownerId, Guid connectionId, string chatId,
        string displayName, bool readAlong, bool autoReminders, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockConnectionAsync(connectionId, cancellationToken);
        var owned = await db.ChannelConnections.AnyAsync(x => x.Id == connectionId && x.OwnerId == ownerId &&
                                                               x.Kind == ChannelKinds.WhatsAppLinked,
            cancellationToken);
        if (!owned) return null;
        var now = time.GetUtcNow();
        var entity = await db.WhatsAppChats.SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.ConnectionId == connectionId && x.ChatId == chatId, cancellationToken);
        if (entity is null)
        {
            entity = new WhatsAppChatEntity
            {
                Id = Guid.CreateVersion7(), OwnerId = ownerId, ConnectionId = connectionId, ChatId = chatId,
                IsGroup = WhatsAppChatIds.IsGroup(chatId), CreatedAt = now
            };
            db.WhatsAppChats.Add(entity);
        }
        // Turning reading or reminders on starts the scan from now, so old history never produces reminders.
        if ((readAlong && !entity.ReadAlong) || (autoReminders && !entity.AutoReminders)) entity.ScannedThrough = now;
        entity.DisplayName = WhatsAppChatIds.CleanName(displayName, chatId);
        entity.ReadAlong = readAlong;
        entity.AutoReminders = autoReminders;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<IReadOnlyList<string>> ListWatchedChatIdsAsync(Guid connectionId,
        CancellationToken cancellationToken) =>
        await db.WhatsAppChats.AsNoTracking().Where(x => x.ConnectionId == connectionId && x.ReadAlong)
            .OrderBy(x => x.ChatId).Select(x => x.ChatId).ToListAsync(cancellationToken);

    public async Task<int> StoreObservedAsync(Guid connectionId, IReadOnlyList<ObservedWhatsAppMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0) return 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockConnectionAsync(connectionId, cancellationToken);
        var chatIds = messages.SelectMany(x => new[] { x.ChatId, x.CanonicalChatId }).OfType<string>().Distinct().ToArray();
        var chats = await db.WhatsAppChats
            .Where(x => x.ConnectionId == connectionId && x.ReadAlong && chatIds.Contains(x.ChatId))
            .ToDictionaryAsync(x => x.ChatId, cancellationToken);
        var now = time.GetUtcNow();
        var inserted = 0;
        foreach (var message in messages)
        {
            WhatsAppChatEntity? chat = null;
            if (message.CanonicalChatId is { } canonical && canonical.StartsWith('+') &&
                !WhatsAppChatIds.IsGroup(message.ChatId)) chats.TryGetValue(canonical, out chat);
            if (chat is null && !chats.TryGetValue(message.ChatId, out chat)) continue;
            var text = message.Text.Length <= 8_000 ? message.Text : message.Text[..8_000];
            var sender = message.Sender is { Length: > 80 } name ? name[..80] : message.Sender;
            var senderId = message.SenderId is { Length: > 100 } longSender ? longSender[..100] : message.SenderId;
            var mediaJson = message.MediaJson;
            var messageId = Guid.CreateVersion7();
            // History is context, not a new arrival. Put it at or before read along began for unread/reminder
            // watermarks, so importing old appointments never schedules them as newly received messages.
            var receivedAt = message.Historical
                ? (message.SentAt < chat.CreatedAt ? message.SentAt : chat.CreatedAt)
                : now;
            var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO whatsapp_messages ("Id", owner_id, connection_id, chat_id, external_id, from_me, sender, sender_id, text, media, sent_at, created_at)
                VALUES ({messageId}, {chat.OwnerId}, {connectionId}, {chat.ChatId}, {message.ExternalId}, {message.FromMe}, {sender}, {senderId}, {text}, CAST({mediaJson} AS jsonb), {message.SentAt}, {receivedAt})
                ON CONFLICT (connection_id, external_id) DO NOTHING
                """, cancellationToken);
            if (rows == 0) continue;
            if (message.Content is { Length: > 0 } content && message.ContentType is not null)
                db.WhatsAppMessageMedia.Add(new WhatsAppMessageMediaEntity
                {
                    MessageId = messageId, OwnerId = chat.OwnerId, ContentType = message.ContentType, Content = content
                });
            inserted++;
            if (chat.LastMessageAt is null || message.SentAt > chat.LastMessageAt) chat.LastMessageAt = message.SentAt;
            if (!message.Historical) chat.LastReceivedAt = now;
        }
        if (inserted > 0) await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<(byte[] Content, string ContentType)?> OpenMediaAsync(Guid ownerId, Guid connectionId,
        Guid messageId, CancellationToken cancellationToken)
    {
        var row = await (
            from blob in db.WhatsAppMessageMedia.AsNoTracking()
            join message in db.WhatsAppMessages.AsNoTracking() on blob.MessageId equals message.Id
            where blob.MessageId == messageId && blob.OwnerId == ownerId && message.OwnerId == ownerId &&
                  message.ConnectionId == connectionId
            select new { blob.Content, blob.ContentType }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Content, row.ContentType);
    }

    public async Task<IReadOnlyList<WhatsAppChatMessage>> ListMessagesAsync(Guid ownerId, Guid connectionId,
        string chatId, int limit, DateTimeOffset? before, CancellationToken cancellationToken, Guid? beforeId = null)
    {
        var query = db.WhatsAppMessages.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ConnectionId == connectionId && x.ChatId == chatId &&
                        (before == null || x.SentAt < before));
        if (beforeId is { } cursorId)
        {
            var cursor = await query.SingleOrDefaultAsync(x => x.Id == cursorId, cancellationToken);
            if (cursor is null) return [];
            query = db.WhatsAppMessages.FromSqlInterpolated($"""
                SELECT * FROM whatsapp_messages
                WHERE owner_id = {ownerId} AND connection_id = {connectionId} AND chat_id = {chatId}
                  AND (sent_at < {cursor.SentAt} OR (sent_at = {cursor.SentAt} AND "Id" < {cursor.Id}))
                """).AsNoTracking();
        }
        return (await query.OrderByDescending(x => x.SentAt).ThenByDescending(x => x.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();
    }

    public async Task<WhatsAppChatMessage?> GetMessageAsync(Guid ownerId, Guid connectionId, string chatId,
        Guid messageId, CancellationToken cancellationToken) =>
        (await db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId &&
            x.ConnectionId == connectionId && x.ChatId == chatId && x.Id == messageId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<WhatsAppSearchHit>> SearchAsync(Guid ownerId, string query, Guid? connectionId,
        string? chatId, int limit, CancellationToken cancellationToken)
    {
        var pattern = "%" + query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var hits = await (
                from message in db.WhatsAppMessages.AsNoTracking()
                join chat in db.WhatsAppChats.AsNoTracking()
                    on new { message.ConnectionId, message.ChatId } equals new { chat.ConnectionId, chat.ChatId }
                where message.OwnerId == ownerId && chat.OwnerId == ownerId &&
                      (connectionId == null || message.ConnectionId == connectionId) &&
                      (chatId == null || message.ChatId == chatId) &&
                      EF.Functions.ILike(message.Text, pattern, "\\")
                orderby message.SentAt descending
                select new { chat, message })
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return hits.Select(x => new WhatsAppSearchHit(x.chat.ToRecord(), x.message.ToRecord())).ToArray();
    }

    public async Task<int> ClearHistoryAsync(Guid ownerId, Guid connectionId, string chatId,
        CancellationToken cancellationToken) =>
        await db.WhatsAppMessages
            .Where(x => x.OwnerId == ownerId && x.ConnectionId == connectionId && x.ChatId == chatId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task SetAskConversationAsync(Guid ownerId, Guid chatSettingsId, Guid conversationId,
        CancellationToken cancellationToken) =>
        await db.WhatsAppChats.Where(x => x.Id == chatSettingsId && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AskConversationId, conversationId),
                cancellationToken);

    public async Task<IReadOnlyList<WhatsAppScanBatch>> ClaimScanBatchesAsync(TimeSpan quiet, int limit,
        int contextSize, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var settledBefore = now - quiet;
        var candidates = await db.WhatsAppChats.AsNoTracking()
            .Where(x => x.ReadAlong && x.AutoReminders && x.LastReceivedAt != null &&
                        x.LastReceivedAt < settledBefore &&
                        (x.ScannedThrough == null || x.LastReceivedAt > x.ScannedThrough) &&
                        (x.ScanLeaseUntil == null || x.ScanLeaseUntil < now))
            .OrderBy(x => x.LastReceivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var batches = new List<WhatsAppScanBatch>();
        foreach (var chat in candidates)
        {
            // Claim with a conditional update so two API replicas never scan the same chat at once.
            var claimed = await db.WhatsAppChats
                .Where(x => x.Id == chat.Id && (x.ScanLeaseUntil == null || x.ScanLeaseUntil < now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ScanLeaseUntil, now + ScanLease),
                    cancellationToken);
            if (claimed == 0) continue;
            var since = chat.ScannedThrough ?? DateTimeOffset.UnixEpoch;
            var fresh = await db.WhatsAppMessages.AsNoTracking()
                .Where(x => x.ConnectionId == chat.ConnectionId && x.ChatId == chat.ChatId && x.CreatedAt > since)
                .OrderBy(x => x.CreatedAt).Take(MaxNewMessagesPerScan)
                .ToListAsync(cancellationToken);
            var scannedThrough = fresh.Count == 0 ? now : fresh.Max(x => x.CreatedAt);
            var firstNew = fresh.Count == 0 ? now : fresh.Min(x => x.SentAt);
            var context = await db.WhatsAppMessages.AsNoTracking()
                .Where(x => x.ConnectionId == chat.ConnectionId && x.ChatId == chat.ChatId && x.CreatedAt <= since &&
                            x.SentAt <= firstNew)
                .OrderByDescending(x => x.SentAt).Take(contextSize)
                .ToListAsync(cancellationToken);
            batches.Add(new WhatsAppScanBatch(chat.ToRecord(),
                context.OrderBy(x => x.SentAt).Select(x => x.ToRecord()).ToArray(),
                fresh.OrderBy(x => x.SentAt).Select(x => x.ToRecord()).ToArray(), scannedThrough));
        }
        return batches;
    }

    public async Task CompleteScanAsync(Guid chatSettingsId, DateTimeOffset scannedThrough,
        CancellationToken cancellationToken) =>
        await db.WhatsAppChats.Where(x => x.Id == chatSettingsId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ScannedThrough, scannedThrough)
                .SetProperty(x => x.ScanLeaseUntil, (DateTimeOffset?)null), cancellationToken);
}
