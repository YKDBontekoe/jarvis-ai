using Jarvis.Application.Settings;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

/// <summary>Owners are the Identity users, so this lists every account regardless of which settings it has saved.</summary>
public sealed class OwnerDirectory(JarvisDbContext db) : IOwnerDirectory
{
    public async Task<IReadOnlyList<Guid>> ListOwnersAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Select(user => user.Id).ToListAsync(cancellationToken);
}
