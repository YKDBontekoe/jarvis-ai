using Jarvis.Application.Game;
using Jarvis.Domain.Game;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class GamePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
    private static readonly DateOnly Monday = new(2026, 11, 9);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public async Task Bets_are_owner_scoped_and_one_subject_has_one_open_bet()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var bet = Bet(owner, "habit:a", now);

        await using (var database = CreateDbContext())
            await new GameRepository(database).AddBetsAsync([bet, Bet(other, "habit:a", now)], default);

        // A second refresh racing the first adds nothing rather than failing.
        await using (var database = CreateDbContext())
        {
            var repository = new GameRepository(database);
            await repository.AddBetsAsync([Bet(owner, "habit:a", now)], default);
            Assert.Single(await repository.ListBetsAsync(owner, default));
            Assert.Null(await repository.GetBetAsync(bet.Id, other, default));
            Assert.False(await repository.UpdateBetAsync(bet with { OwnerId = other, Status = BetStatuses.Won }, default));

            Assert.True(await repository.UpdateBetAsync(bet with { Status = BetStatuses.Void, ResolvedAt = now }, default));
            Assert.Equal(BetStatuses.Void, (await repository.GetBetAsync(bet.Id, owner, default))!.Status);

            // Once settled, the same subject can be bet on again.
            await repository.AddBetsAsync([Bet(owner, "habit:a", now)], default);
            Assert.Equal(2, (await repository.ListBetsAsync(owner, default)).Count);
        }
    }

    [Fact]
    public async Task A_weeks_quests_are_written_once()
    {
        var owner = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var quest = Quest(owner, "journal:2026-11-09", now);

        await using (var database = CreateDbContext())
            await new GameRepository(database).AddQuestsAsync([quest, Quest(owner, "habit:a", now)], default);

        await using (var database = CreateDbContext())
        {
            var repository = new GameRepository(database);
            await repository.AddQuestsAsync([Quest(owner, "journal:2026-11-09", now)], default);
            Assert.Equal(2, (await repository.ListQuestsAsync(owner, default)).Count);
            Assert.Empty(await repository.ListQuestsAsync(Guid.CreateVersion7(), default));

            Assert.True(await repository.UpdateQuestAsync(quest with
            {
                Status = QuestStatuses.Done, CompletedAt = now, UpdatedAt = now
            }, default));
            var stored = (await repository.GetQuestAsync(quest.Id, owner, default))!;
            Assert.Equal(QuestStatuses.Done, stored.Status);
            Assert.NotNull(stored.CompletedAt);
            Assert.Equal(Monday, stored.WeekStart);
        }
    }

    private static GameBet Bet(Guid owner, string subject, DateTimeOffset now) => new(Guid.CreateVersion7(), owner,
        BetKinds.HabitDay, subject, null, "You’ll skip Exercise on Thursday", "evidence", 0.8, 68,
        Monday.AddDays(3), Monday.AddDays(3), BetStatuses.Open, null, now, now);

    private static GameQuest Quest(Guid owner, string subject, DateTimeOffset now) => new(Guid.CreateVersion7(), owner,
        Monday, QuestKinds.Journal, subject, null, "Write in your journal on 3 days", "detail", 3, 20, false,
        QuestStatuses.Open, null, now, now);

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
