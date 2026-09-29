using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence.Configurations.Infrastructure;

/// <summary>
/// PostgreSQL extensions and ASP.NET Identity table naming. See Configurations/README.md for where to add new mappings.
/// </summary>
internal static class JarvisPersistenceInfrastructureConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }
}
