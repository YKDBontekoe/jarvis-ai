using Jarvis.Application.Agents;
using Jarvis.Application.Browser;
using Jarvis.Application.Surfaces;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class UiSurfaceEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConversationId { get; set; }
    public string Kind { get; set; } = UiSurfaceKinds.Card;
    public string Title { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = "{}";
    public string Status { get; set; } = "open";
    public string? CompletedAction { get; set; }
    public string? ValuesJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UiSurfaceRecord ToRecord() => new(Id, OwnerId, ConversationId, Kind, Title, SchemaJson, Status, CreatedAt,
        UpdatedAt);
}

public sealed class RemoteAgentEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public RemoteAgentRecord ToRecord() => new(Id, OwnerId, Name, Url, Enabled, LastUsedAt, LastError, CreatedAt);
}

public sealed class A2ATokenEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public A2ATokenRecord ToRecord() => new(Id, Name, CreatedAt, LastUsedAt);
}

public sealed class BrowserSessionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ConversationId { get; set; }
    public string Goal { get; set; } = string.Empty;
    public string? StartUrl { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<BrowserStepEntity> Steps { get; set; } = [];

    public BrowserSessionRecord ToRecord() => new(Id, OwnerId, ConversationId, Goal, StartUrl, Status, CreatedAt,
        UpdatedAt, Steps.OrderBy(step => step.Ordinal).Select(step => step.ToRecord()).ToArray());
}

public sealed class BrowserStepEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int Ordinal { get; set; }
    public string Tool { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public BrowserStepRecord ToRecord() => new(Id, Ordinal, Tool, Summary, Success, CreatedAt);
}

public sealed class UiSurfaceRepository(JarvisDbContext db) : IUiSurfaceRepository
{
    public async Task<UiSurfaceRecord> CreateAsync(Guid ownerId, Guid conversationId, string kind, string title,
        string schemaJson, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new UiSurfaceEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            ConversationId = conversationId,
            Kind = kind,
            Title = title,
            SchemaJson = schemaJson,
            Status = "open",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.UiSurfaces.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<UiSurfaceRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await db.UiSurfaces.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<UiSurfaceRecord>> ListForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken) =>
        (await db.UiSurfaces.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<UiSurfaceRecord?> CompleteAsync(Guid ownerId, Guid id, string actionId, string valuesJson,
        CancellationToken cancellationToken)
    {
        var entity = await db.UiSurfaces.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null) return null;
        entity.Status = "completed";
        entity.CompletedAction = actionId;
        entity.ValuesJson = valuesJson;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task ReopenAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.UiSurfaces.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null || entity.Status != "completed") return;
        entity.Status = "open";
        entity.CompletedAction = null;
        entity.ValuesJson = null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RemoteAgentRepository(JarvisDbContext db) : IRemoteAgentRepository
{
    public async Task<IReadOnlyList<RemoteAgentRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.RemoteAgents.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.Name)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<RemoteAgentRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await db.RemoteAgents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<RemoteAgentRecord> CreateAsync(Guid ownerId, string name, string url, bool enabled,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new RemoteAgentEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name,
            Url = url,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.RemoteAgents.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<RemoteAgentRecord?> UpdateAsync(Guid ownerId, Guid id, string name, string url, bool enabled,
        CancellationToken cancellationToken)
    {
        var entity = await db.RemoteAgents.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null) return null;
        entity.Name = name;
        entity.Url = url;
        entity.Enabled = enabled;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.RemoteAgents.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null) return false;
        db.RemoteAgents.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RecordUseAsync(Guid ownerId, Guid id, string? error, CancellationToken cancellationToken)
    {
        var entity = await db.RemoteAgents.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null) return;
        entity.LastUsedAt = DateTimeOffset.UtcNow;
        entity.LastError = error is { Length: > 500 } ? error[..500] : error;
        entity.UpdatedAt = entity.LastUsedAt.Value;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class A2ATokenRepository(JarvisDbContext db) : IA2ATokenRepository
{
    public async Task<IReadOnlyList<A2ATokenRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.A2ATokens.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<(A2ATokenRecord Record, string Token)> CreateAsync(Guid ownerId, string name,
        CancellationToken cancellationToken)
    {
        var token = "jarvis-a2a-" + Convert.ToHexStringLower(Guid.CreateVersion7().ToByteArray()) +
                    Convert.ToHexStringLower(Guid.CreateVersion7().ToByteArray())[..16];
        var entity = new A2ATokenEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name,
            TokenHash = Hash(token),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.A2ATokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (entity.ToRecord() with { Token = token }, token);
    }

    public async Task<Guid?> FindOwnerAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        var entity = await db.A2ATokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (entity is null) return null;
        entity.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return entity.OwnerId;
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.A2ATokens.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (entity is null) return false;
        db.A2ATokens.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static string Hash(string token) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}

public sealed class BrowserSessionRepository(JarvisDbContext db) : IBrowserSessionStore
{
    public async Task<BrowserSessionRecord> StartAsync(Guid ownerId, Guid conversationId, string goal, string? startUrl,
        CancellationToken cancellationToken)
    {
        var active = await db.BrowserSessions
            .Where(x => x.OwnerId == ownerId && x.ConversationId == conversationId && x.Status == "active")
            .ToListAsync(cancellationToken);
        foreach (var previous in active)
        {
            previous.Status = "superseded";
            previous.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new BrowserSessionEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            ConversationId = conversationId,
            Goal = goal,
            StartUrl = startUrl,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.BrowserSessions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<BrowserSessionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await db.BrowserSessions.AsNoTracking().Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<BrowserSessionRecord?> GetActiveForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken) =>
        (await db.BrowserSessions.AsNoTracking().Include(x => x.Steps)
            .Where(x => x.OwnerId == ownerId && x.ConversationId == conversationId && x.Status == "active")
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<BrowserSessionRecord>> ListForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken) =>
        (await db.BrowserSessions.AsNoTracking().Include(x => x.Steps)
            .Where(x => x.OwnerId == ownerId && x.ConversationId == conversationId)
            .OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task RecordStepAsync(Guid sessionId, string tool, string summary, bool success,
        CancellationToken cancellationToken)
    {
        var session = await db.BrowserSessions.Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null) return;
        session.Steps.Add(new BrowserStepEntity
        {
            Id = Guid.CreateVersion7(),
            SessionId = sessionId,
            Ordinal = session.Steps.Count + 1,
            Tool = tool.Length > 80 ? tool[..80] : tool,
            Summary = summary.Length > 1_000 ? summary[..1_000] : summary,
            Success = success,
            CreatedAt = DateTimeOffset.UtcNow
        });
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid sessionId, string status, CancellationToken cancellationToken)
    {
        var session = await db.BrowserSessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null) return;
        session.Status = status;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
