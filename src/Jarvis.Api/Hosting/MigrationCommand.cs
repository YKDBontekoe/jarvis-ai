using Jarvis.Infrastructure.Persistence;
using Jarvis.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Sentry;

namespace Jarvis.Api.Hosting;

internal static class MigrationCommand
{
    // Stable, application-specific key. The session lock prevents two deploy hosts from migrating together.
    private const long AdvisoryLockKey = 0x4A_41_52_56_49_53;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        JarvisSentry.Add(builder);
        var connectionString = builder.Configuration.GetConnectionString("jarvis")
            ?? throw new InvalidOperationException("PostgreSQL connection string 'jarvis' is required.");

        builder.Services.AddDbContext<JarvisDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<JarvisDbContext>().Database;
            await database.OpenConnectionAsync(cancellationToken);
            try
            {
                await database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_lock({0})", new object[] { AdvisoryLockKey }, cancellationToken);
                var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                if (pending.Length == 0)
                {
                    logger.LogInformation("Database schema is current; no migrations are pending.");
                    return 0;
                }

                logger.LogInformation("Applying {MigrationCount} pending migration(s).", pending.Length);
                // A branch can add a migration older than ones already applied. Using
                // the last pending ID as a target would revert those newer migrations.
                // The default target applies missing migrations through the latest ID.
                await database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migration completed; schema is at the latest migration.");
                return 0;
            }
            finally
            {
                await database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_unlock({0})", new object[] { AdvisoryLockKey }, CancellationToken.None);
                await database.CloseConnectionAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Database migration failed; application services were not updated.");
            SentrySdk.CaptureException(exception);
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
            return 1;
        }
    }
}
