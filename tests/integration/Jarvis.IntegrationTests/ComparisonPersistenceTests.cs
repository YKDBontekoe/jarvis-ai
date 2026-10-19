using Jarvis.Application.Comparisons;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class ComparisonPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public async Task A_comparison_round_trips_with_unknowns_dates_and_sources_and_stays_with_its_owner()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var definition = new ComparisonDefinition("Boiler", "Which boiler?",
            [new ComparisonCriterion("price", "Price", 8), new ComparisonCriterion("quiet", "Noise", 2)],
            [
                new ComparisonOption("alpha", "Alpha", "The cheap one", new Dictionary<string, ComparisonCell>
                {
                    ["price"] = new(9, "€1,900", "quote-a.pdf", new DateOnly(2026, 10, 1)),
                    ["quiet"] = new(null)
                }),
                new ComparisonOption("beta", "Beta", null, new Dictionary<string, ComparisonCell>())
            ], "alpha", Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var record = new ComparisonRecord(Guid.CreateVersion7(), owner, definition, null, null, now, now);

        await using var database = CreateDbContext();
        var repository = new ComparisonRepository(database);
        await repository.AddAsync(record, CancellationToken.None);

        await using var fresh = CreateDbContext();
        var loaded = await new ComparisonRepository(fresh).GetAsync(record.Id, owner, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(definition.DecisionId, loaded.Definition.DecisionId);
        var cell = loaded.Definition.Options[0].Cells["price"];
        Assert.Equal(9, cell.Score);
        Assert.Equal(new DateOnly(2026, 10, 1), cell.AsOf);
        Assert.Equal("quote-a.pdf", cell.Source);
        Assert.Null(loaded.Definition.Options[0].Cells["quiet"].Score);
        // The same definition gives the same ranking after a round trip.
        Assert.Equal(ComparisonCalculator.Evaluate(definition).Rows.Select(row => (row.OptionId, row.Score)),
            ComparisonCalculator.Evaluate(loaded.Definition).Rows.Select(row => (row.OptionId, row.Score)));

        Assert.Null(await repository.GetAsync(record.Id, other, CancellationToken.None));
        Assert.Empty(await repository.ListAsync(other, CancellationToken.None));
        Assert.False(await repository.UpdateAsync(record with { OwnerId = other }, CancellationToken.None));
        Assert.False(await repository.DeleteAsync(record.Id, other, CancellationToken.None));
        Assert.Equal(1, await repository.CountAsync(owner, CancellationToken.None));
        Assert.True(await repository.DeleteAsync(record.Id, owner, CancellationToken.None));
    }

    private JarvisDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);
}
