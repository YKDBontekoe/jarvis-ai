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

public sealed class OwnerSettingsStore(JarvisDbContext db) : IOwnerSettingsStore
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
}
