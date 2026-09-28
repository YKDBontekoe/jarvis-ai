using System.Security.Cryptography;
using System.Text.Json;
using Jarvis.Application.Channels;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ChannelConnectionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = ChannelKinds.Signal;
    public string DisplayName { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string AllowedSendersJson { get; set; } = "[]";
    public bool ForwardNotifications { get; set; }
    public string? NotifyRecipient { get; set; }
    public string WebhookKey { get; set; } = string.Empty;
    public DateTimeOffset? LastInboundAt { get; set; }
    public DateTimeOffset? LastOutboundAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset NotificationsForwardedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ChannelConnectionRecord ToRecord() => new(Id, OwnerId, Kind, DisplayName, Account, Enabled,
        JsonSerializer.Deserialize<string[]>(AllowedSendersJson) ?? [], ForwardNotifications, NotifyRecipient,
        WebhookKey, LastInboundAt, LastOutboundAt, LastError, CreatedAt, UpdatedAt, NotificationsForwardedUntil);
}

public sealed class ChannelMessageEntity
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string Direction { get; set; } = "in";
    public string Peer { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}

public sealed class ChannelThreadEntity
{
    public Guid ConnectionId { get; set; }
    public string Peer { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChannelRepository(JarvisDbContext db) : IChannelRepository
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<ChannelConnectionRecord>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.ChannelConnections.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<IReadOnlyList<ChannelConnectionRecord>> ListEnabledAsync(string? kind,
        CancellationToken cancellationToken) =>
        (await db.ChannelConnections.AsNoTracking().Where(x => x.Enabled && (kind == null || x.Kind == kind))
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<ChannelConnectionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await db.ChannelConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task<ChannelConnectionRecord?> FindByWebhookKeyAsync(string webhookKey,
        CancellationToken cancellationToken) =>
        (await db.ChannelConnections.AsNoTracking().SingleOrDefaultAsync(x => x.WebhookKey == webhookKey,
            cancellationToken))?.ToRecord();

    public async Task<ChannelConnectionRecord> CreateAsync(Guid ownerId, SaveChannelRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new ChannelConnectionEntity
        {
            Id = Guid.CreateVersion7(), OwnerId = ownerId, Kind = request.Kind!, CreatedAt = now,
            WebhookKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)),
            NotificationsForwardedUntil = now
        };
        Apply(entity, request, now);
        db.ChannelConnections.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<ChannelConnectionRecord?> UpdateAsync(Guid ownerId, Guid id, SaveChannelRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.ChannelConnections.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null) return null;
        var wasEnabled = entity.Enabled;
        Apply(entity, request, DateTimeOffset.UtcNow);
        if (!wasEnabled && entity.Enabled) entity.NotificationsForwardedUntil = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        await db.ChannelConnections.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> EnqueueInboundAsync(Guid connectionId, string sender, string text, string externalId,
        CancellationToken cancellationToken)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO channel_messages ("Id", connection_id, direction, peer, text, external_id, status, created_at)
            VALUES ({Guid.CreateVersion7()}, {connectionId}, 'in', {sender}, {text}, {externalId}, 'pending', {DateTimeOffset.UtcNow})
            ON CONFLICT (connection_id, external_id) WHERE external_id IS NOT NULL DO NOTHING
            """, cancellationToken);
        if (inserted > 0)
            await db.ChannelConnections.Where(x => x.Id == connectionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastInboundAt, DateTimeOffset.UtcNow),
                    cancellationToken);
        return inserted > 0;
    }

    public async Task<IReadOnlyList<InboundChannelMessage>> ClaimPendingInboundAsync(int limit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.ChannelMessages.FromSqlInterpolated($"""
                SELECT * FROM channel_messages
                WHERE direction = 'in' AND status = 'pending' AND (lease_until IS NULL OR lease_until < {now})
                ORDER BY created_at
                LIMIT {limit}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        foreach (var message in claimed) message.LeaseUntil = now + Lease;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var ids = claimed.Select(x => x.ConnectionId).Distinct().ToArray();
        var connections = await db.ChannelConnections.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return claimed.Where(message => connections.ContainsKey(message.ConnectionId))
            .Select(message => new InboundChannelMessage(message.Id, connections[message.ConnectionId].ToRecord(),
                message.Peer, message.Text))
            .ToArray();
    }

    public Task CompleteInboundAsync(Guid messageId, string? error, CancellationToken cancellationToken) =>
        db.ChannelMessages.Where(x => x.Id == messageId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, error == null ? "processed" : "failed")
            .SetProperty(x => x.Error, error)
            .SetProperty(x => x.ProcessedAt, DateTimeOffset.UtcNow)
            .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), cancellationToken);

    public async Task RecordOutboundAsync(Guid connectionId, string recipient, string text, string? error,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        db.ChannelMessages.Add(new ChannelMessageEntity
        {
            Id = Guid.CreateVersion7(), ConnectionId = connectionId, Direction = "out", Peer = recipient,
            Text = text.Length <= 8_000 ? text : text[..8_000], Status = error == null ? "sent" : "failed",
            Error = error, CreatedAt = now, ProcessedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await db.ChannelConnections.Where(x => x.Id == connectionId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.LastOutboundAt, x => error == null ? now : x.LastOutboundAt)
            .SetProperty(x => x.LastError, error), cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelMessageRecord>> ListMessagesAsync(Guid ownerId, Guid connectionId, int limit,
        CancellationToken cancellationToken)
    {
        if (!await db.ChannelConnections.AnyAsync(x => x.Id == connectionId && x.OwnerId == ownerId, cancellationToken))
            return [];
        return (await db.ChannelMessages.AsNoTracking().Where(x => x.ConnectionId == connectionId)
                .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken))
            .Select(ToMessage).ToArray();
    }

    public async Task<IReadOnlyList<ChannelThreadRecord>> ListThreadsAsync(Guid ownerId, Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (!await db.ChannelConnections.AnyAsync(x => x.Id == connectionId && x.OwnerId == ownerId, cancellationToken))
            return [];
        var messages = await db.ChannelMessages.AsNoTracking().Where(x => x.ConnectionId == connectionId)
            .OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(cancellationToken);
        var conversations = await db.ChannelThreads.AsNoTracking().Where(x => x.ConnectionId == connectionId)
            .ToDictionaryAsync(x => x.Peer, x => x.ConversationId, cancellationToken);
        return messages.GroupBy(x => x.Peer)
            .Select(group =>
            {
                var last = group.First();
                return new ChannelThreadRecord(group.Key,
                    conversations.GetValueOrDefault(group.Key),
                    group.Count(),
                    ToMessage(last));
            })
            .OrderByDescending(thread => thread.LastMessage?.CreatedAt)
            .ToArray();
    }

    public async Task<IReadOnlyList<ChannelMessageRecord>> ListThreadMessagesAsync(Guid ownerId, Guid connectionId,
        string peer, int limit, CancellationToken cancellationToken)
    {
        if (!await db.ChannelConnections.AnyAsync(x => x.Id == connectionId && x.OwnerId == ownerId, cancellationToken))
            return [];
        var normalized = ChannelAddresses.Normalize(peer);
        return (await db.ChannelMessages.AsNoTracking()
                .Where(x => x.ConnectionId == connectionId && (x.Peer == peer || x.Peer == normalized))
                .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken))
            .Select(ToMessage).ToArray();
    }

    private static ChannelMessageRecord ToMessage(ChannelMessageEntity x) =>
        new(x.Id, x.ConnectionId, x.Direction, x.Peer, x.Text, x.Status, x.CreatedAt, x.ProcessedAt, x.Error);

    public async Task<Guid?> GetThreadConversationAsync(Guid connectionId, string sender,
        CancellationToken cancellationToken) =>
        await db.ChannelThreads.AsNoTracking().Where(x => x.ConnectionId == connectionId && x.Peer == sender)
            .Select(x => (Guid?)x.ConversationId).SingleOrDefaultAsync(cancellationToken);

    public async Task SetThreadConversationAsync(Guid connectionId, string sender, Guid conversationId,
        CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO channel_threads (connection_id, peer, conversation_id, updated_at)
            VALUES ({connectionId}, {sender}, {conversationId}, {DateTimeOffset.UtcNow})
            ON CONFLICT (connection_id, peer) DO UPDATE SET conversation_id = EXCLUDED.conversation_id,
                updated_at = EXCLUDED.updated_at
            """, cancellationToken);

    public Task AdvanceNotificationWatermarkAsync(Guid connectionId, DateTimeOffset forwardedUntil,
        CancellationToken cancellationToken) =>
        db.ChannelConnections.Where(x => x.Id == connectionId && x.NotificationsForwardedUntil < forwardedUntil)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NotificationsForwardedUntil, forwardedUntil),
                cancellationToken);

    private static void Apply(ChannelConnectionEntity entity, SaveChannelRequest request, DateTimeOffset now)
    {
        entity.DisplayName = request.DisplayName!.Trim();
        entity.Account = request.Account!.Trim();
        entity.Enabled = request.Enabled;
        entity.AllowedSendersJson = JsonSerializer.Serialize(request.AllowedSenders ?? []);
        entity.ForwardNotifications = request.ForwardNotifications;
        entity.NotifyRecipient = string.IsNullOrWhiteSpace(request.NotifyRecipient) ? null : request.NotifyRecipient;
        entity.UpdatedAt = now;
    }
}
