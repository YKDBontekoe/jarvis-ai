using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class MigrationUpgradeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Previous_release_data_survives_upgrade_and_migrations_are_idempotent()
    {
        await using var database = new JarvisDbContext(new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), options => options.UseVector()).Options);
        var migrations = database.Database.GetMigrations().ToArray();
        var baseline = Environment.GetEnvironmentVariable("JARVIS_UPGRADE_MIGRATION") ?? migrations[^2];
        Assert.Contains(baseline, migrations);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync(baseline);
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        var message = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO conversations ("Id", "OwnerId", "Title", "CreatedAt", "UpdatedAt")
            VALUES ({conversation}, {owner}, 'Upgrade fixture', {now}, {now});
            """);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO messages ("Id", "ConversationId", "Role", "Content", "CreatedAt")
            VALUES ({message}, {conversation}, 'user', 'Retain this history', {now});
            """);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        var store = new ConversationStore(database);
        var record = await store.GetAsync(conversation, owner, CancellationToken.None);
        Assert.NotNull(record);
        Assert.Equal("Upgrade fixture", record.Title);
        Assert.Null(await store.GetAsync(conversation, otherOwner, CancellationToken.None));
        Assert.Contains(await store.GetMessagesAsync(conversation, CancellationToken.None),
            value => value.Id == message && value.Content == "Retain this history");
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
    }
}
