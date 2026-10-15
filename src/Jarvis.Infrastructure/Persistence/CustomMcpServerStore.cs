using System.Text.Json;
using Jarvis.Application.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class CustomMcpServerEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RegistryServerId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string DraftSource { get; set; } = string.Empty;
    public string DraftSha256 { get; set; } = string.Empty;
    public string? ActiveSource { get; set; }
    public string? ActiveSha256 { get; set; }
    public int? ActiveRevision { get; set; }
    public string SecretsJson { get; set; } = "[]";
    public string? LastTestJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CustomMcpServerRevisionEntity> Revisions { get; set; } = [];
}

public sealed class CustomMcpServerRevisionEntity
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public int Revision { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CustomMcpServerStore(JarvisDbContext db) : ICustomMcpServerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CustomMcpServerSummary>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.CustomMcpServers.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.Name)
            .ToListAsync(cancellationToken)).Select(ToSummary).ToArray();

    public async Task<CustomMcpServerDetail?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.CustomMcpServers.AsNoTracking().Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id, cancellationToken);
        if (entity is null) return null;
        var revisions = entity.Revisions.OrderByDescending(x => x.Revision)
            .Select(x => new CustomMcpRevision(x.Revision, x.Sha256, x.CreatedAt, x.Revision == entity.ActiveRevision))
            .ToArray();
        return new CustomMcpServerDetail(ToSummary(entity), entity.DraftSource, entity.ActiveSource, revisions);
    }

    public async Task<CustomMcpServerSummary?> FindByNameAsync(Guid ownerId, string name,
        CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        var entity = await db.CustomMcpServers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Name.ToLower() == lowered, cancellationToken);
        return entity is null ? null : ToSummary(entity);
    }

    public async Task<CustomMcpServerSummary?> FindByRegistryIdAsync(Guid ownerId, string registryServerId,
        CancellationToken cancellationToken)
    {
        var entity = await db.CustomMcpServers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.RegistryServerId == registryServerId,
                cancellationToken);
        return entity is null ? null : ToSummary(entity);
    }

    public async Task<CustomMcpServerSummary> CreateAsync(Guid ownerId, Guid id, string registryServerId,
        string name, string? description, string source, string sha256, IReadOnlyList<McpSecretBinding> secrets,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new CustomMcpServerEntity
        {
            Id = id,
            OwnerId = ownerId,
            Name = name,
            Description = description,
            RegistryServerId = registryServerId,
            Revision = 1,
            DraftSource = source,
            DraftSha256 = sha256,
            SecretsJson = JsonSerializer.Serialize(secrets, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };
        entity.Revisions.Add(NewRevision(id, 1, source, sha256, now));
        db.CustomMcpServers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<CustomMcpServerSummary?> SaveDraftAsync(Guid ownerId, Guid id, string? description,
        string source, string sha256, IReadOnlyList<McpSecretBinding> secrets, CancellationToken cancellationToken)
    {
        var entity = await db.CustomMcpServers.Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id, cancellationToken);
        if (entity is null) return null;
        var now = DateTimeOffset.UtcNow;
        entity.Description = description;
        entity.SecretsJson = JsonSerializer.Serialize(secrets, JsonOptions);
        entity.UpdatedAt = now;
        if (entity.DraftSha256 != sha256)
        {
            entity.Revision += 1;
            entity.DraftSource = source;
            entity.DraftSha256 = sha256;
            var revision = NewRevision(id, entity.Revision, source, sha256, now);
            entity.Revisions.Add(revision);
            db.Add(revision);
            Trim(entity);
        }
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<CustomMcpServerSummary?> ActivateAsync(Guid ownerId, Guid id, string expectedSha256,
        CancellationToken cancellationToken)
    {
        var entity = await db.CustomMcpServers
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id, cancellationToken);
        if (entity is null || entity.DraftSha256 != expectedSha256) return null;
        entity.ActiveSource = entity.DraftSource;
        entity.ActiveSha256 = entity.DraftSha256;
        entity.ActiveRevision = entity.Revision;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<CustomMcpServerSummary?> RevertDraftAsync(Guid ownerId, Guid id, int revision,
        CancellationToken cancellationToken)
    {
        var entity = await db.CustomMcpServers.Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id, cancellationToken);
        var target = entity?.Revisions.SingleOrDefault(x => x.Revision == revision);
        if (entity is null || target is null) return null;
        if (entity.DraftSha256 == target.Sha256) return ToSummary(entity);
        var now = DateTimeOffset.UtcNow;
        entity.Revision += 1;
        entity.DraftSource = target.Source;
        entity.DraftSha256 = target.Sha256;
        entity.UpdatedAt = now;
        var copy = NewRevision(id, entity.Revision, target.Source, target.Sha256, now);
        entity.Revisions.Add(copy);
        db.Add(copy);
        Trim(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task RecordTestAsync(Guid ownerId, Guid id, CustomMcpTestResult result,
        CancellationToken cancellationToken)
    {
        var entity = await db.CustomMcpServers
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id, cancellationToken);
        if (entity is null) return;
        entity.LastTestJson = JsonSerializer.Serialize(result, JsonOptions);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(string Source, string Sha256)?> GetActiveSourceAsync(Guid ownerId, Guid id,
        CancellationToken cancellationToken)
    {
        var active = await db.CustomMcpServers.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.Id == id && x.ActiveSource != null && x.ActiveSha256 != null)
            .Select(x => new { x.ActiveSource, x.ActiveSha256 })
            .SingleOrDefaultAsync(cancellationToken);
        return active is null ? null : (active.ActiveSource!, active.ActiveSha256!);
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        await db.CustomMcpServers.Where(x => x.OwnerId == ownerId && x.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private void Trim(CustomMcpServerEntity entity)
    {
        var drop = CustomMcpServerManager.RevisionsToTrim(entity.Revisions.Select(x => x.Revision).ToArray(),
            entity.ActiveRevision).ToHashSet();
        foreach (var revision in entity.Revisions.Where(x => drop.Contains(x.Revision)).ToArray())
        {
            entity.Revisions.Remove(revision);
            db.Remove(revision);
        }
    }

    private static CustomMcpServerRevisionEntity NewRevision(Guid serverId, int revision, string source,
        string sha256, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ServerId = serverId,
        Revision = revision,
        Source = source,
        Sha256 = sha256,
        CreatedAt = now
    };

    private static CustomMcpServerSummary ToSummary(CustomMcpServerEntity entity) => new(entity.Id, entity.Name,
        entity.Description, entity.Revision, entity.DraftSha256, entity.ActiveSha256, entity.ActiveRevision,
        entity.RegistryServerId,
        JsonSerializer.Deserialize<McpSecretBinding[]>(entity.SecretsJson, JsonOptions) ?? [],
        entity.LastTestJson is null ? null : JsonSerializer.Deserialize<CustomMcpTestResult>(entity.LastTestJson, JsonOptions),
        entity.CreatedAt, entity.UpdatedAt);
}
