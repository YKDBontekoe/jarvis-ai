using System.Text.Json;
using Jarvis.Application.Proactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ProactiveCaseEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Document { get; set; } = "{}";
    public long Revision { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }
}

internal sealed class ProactiveCaseConfiguration : IEntityTypeConfiguration<ProactiveCaseEntity>
{
    public void Configure(EntityTypeBuilder<ProactiveCaseEntity> builder)
    {
        builder.ToTable("proactive_cases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        builder.Property(x => x.Document).HasColumnName("document").HasColumnType("jsonb");
        builder.Property(x => x.Revision).HasColumnName("revision");
        builder.Property(x => x.NextCheckAt).HasColumnName("next_check_at");
        builder.HasIndex(x => new { x.OwnerId, x.Fingerprint }).IsUnique();
        builder.HasIndex(x => x.NextCheckAt);
    }
}

public sealed class ProactiveRepository(JarvisDbContext db) : IProactiveRepository, IOwnerSettingDocuments
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static ProactiveCase Read(ProactiveCaseEntity entity) => JsonSerializer.Deserialize<ProactiveCase>(entity.Document, JsonOptions)!;

    public async Task<ProactiveCase> CreateAsync(ProactiveCase value, CancellationToken ct)
    {
        var json = ProactiveRules.Serialize(value);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO proactive_cases(id, owner_id, fingerprint, document, revision, next_check_at)
            VALUES ({value.Id}, {value.OwnerId}, {value.Fingerprint}, CAST({json} AS jsonb), {value.Revision}, {value.NextCheckAt})
            ON CONFLICT(owner_id, fingerprint) DO NOTHING
            """, ct);
        return Read(await db.Set<ProactiveCaseEntity>().AsNoTracking().SingleAsync(x => x.OwnerId == value.OwnerId && x.Fingerprint == value.Fingerprint, ct));
    }

    public async Task<ProactiveCase?> GetAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var entity = await db.Set<ProactiveCaseEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct);
        return entity is null ? null : Read(entity);
    }

    public async Task<IReadOnlyList<ProactiveCase>> ListAsync(Guid ownerId, CancellationToken ct) =>
        (await db.Set<ProactiveCaseEntity>().AsNoTracking().Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.Id).Take(300).ToListAsync(ct)).Select(Read).ToArray();

    public async Task<IReadOnlyList<ProactiveCase>> DueAsync(DateTimeOffset now, CancellationToken ct) =>
        (await db.Set<ProactiveCaseEntity>().AsNoTracking().Where(x => x.NextCheckAt <= now).OrderBy(x => x.NextCheckAt).Take(100).ToListAsync(ct)).Select(Read).ToArray();

    public async Task<bool> CompareExchangeAsync(ProactiveCase value, long expectedRevision, CancellationToken ct)
    {
        var json = ProactiveRules.Serialize(value);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE proactive_cases SET document = CAST({json} AS jsonb), revision = {value.Revision}, next_check_at = {value.NextCheckAt}
            WHERE id = {value.Id} AND owner_id = {value.OwnerId} AND revision = {expectedRevision}
            """, ct) == 1;
    }

    public Task<string?> ReadDocumentAsync(Guid ownerId, string section, CancellationToken ct) =>
        db.OwnerSettings.AsNoTracking().Where(x => x.OwnerId == ownerId && x.Section == section).Select(x => x.ValueJson).SingleOrDefaultAsync(ct);

    public async Task<bool> ApplyAsync(ProactiveCase value, long expectedRevision, ProactiveSettingMutation mutation, CancellationToken ct)
    {
        // Only explicitly supported internal documents can be changed by a proactive check.
        if (mutation.Section != "planner.day") throw new ArgumentException("Unsupported internal change.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Set<ProactiveCaseEntity>().FromSqlInterpolated($"""
            SELECT * FROM proactive_cases WHERE id = {value.Id} AND owner_id = {value.OwnerId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (locked?.Revision != expectedRevision) return false;
        var now = DateTimeOffset.UtcNow;
        int changed;
        if (mutation.ExpectedJson is null)
            changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO owner_settings(owner_id, section, value, updated_at)
                VALUES ({value.OwnerId}, {mutation.Section}, CAST({mutation.AfterJson} AS jsonb), {now})
                ON CONFLICT(owner_id, section) DO NOTHING
                """, ct);
        else
            changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE owner_settings SET value = CAST({mutation.AfterJson} AS jsonb), updated_at = {now}
                WHERE owner_id = {value.OwnerId} AND section = {mutation.Section} AND value = CAST({mutation.ExpectedJson} AS jsonb)
                """, ct);
        if (changed != 1 || !await CompareExchangeAsync(value, expectedRevision, ct)) return false;
        await transaction.CommitAsync(ct);
        return true;
    }
}
