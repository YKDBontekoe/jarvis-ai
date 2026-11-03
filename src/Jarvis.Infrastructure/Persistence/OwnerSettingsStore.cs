using System.Text.Json;
using Jarvis.Application.Settings;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class OwnerSettingEntity
{
    public Guid OwnerId { get; set; }
    public string Section { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OwnerSettingsStore(JarvisDbContext db) : IOwnerSettingsStore, IAtomicOwnerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken cancellationToken)
        where T : class
    {
        var entity = await db.OwnerSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Section == section, cancellationToken);
        return entity is null ? null : JsonSerializer.Deserialize<T>(entity.ValueJson, JsonOptions);
    }

    public async Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken cancellationToken)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO owner_settings (owner_id, section, value, updated_at)
            VALUES ({ownerId}, {section}, CAST({json} AS jsonb), {now})
            ON CONFLICT (owner_id, section) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken cancellationToken) =>
        await db.OwnerSettings.AsNoTracking().Where(x => x.Section == section).Select(x => x.OwnerId)
            .ToListAsync(cancellationToken);

    public Task<string?> ReadJsonAsync(Guid ownerId, string section, CancellationToken ct) =>
        db.OwnerSettings.AsNoTracking().Where(x => x.OwnerId == ownerId && x.Section == section).Select(x => x.ValueJson).SingleOrDefaultAsync(ct);

    public async Task<bool> CompareExchangeJsonAsync(Guid ownerId, string section, string? expectedJson, string afterJson, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (expectedJson is null)
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO owner_settings(owner_id, section, value, updated_at)
                VALUES ({ownerId}, {section}, CAST({afterJson} AS jsonb), {now})
                ON CONFLICT(owner_id, section) DO NOTHING
                """, ct) == 1;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE owner_settings SET value = CAST({afterJson} AS jsonb), updated_at = {now}
            WHERE owner_id = {ownerId} AND section = {section} AND value = CAST({expectedJson} AS jsonb)
            """, ct) == 1;
    }
}
