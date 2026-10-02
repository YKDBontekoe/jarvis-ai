using Jarvis.Application.Reviews;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class WeeklyReviewRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Collects_the_week_and_delivers_it_once()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var week = WeeklyReviewClock.CurrentWeekStart(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        await using (var seed = CreateDbContext())
        {
            seed.JournalEntries.AddRange(
                Entry(owner, week, mood: 4, rating: 7, tags: ["run"]),
                Entry(owner, week.AddDays(2), mood: 5, rating: 9, tags: ["run", "family"]),
                Entry(owner, week.AddDays(-3), mood: 2, rating: 4, tags: []),
                Entry(other, week, mood: 1, rating: 1, tags: ["other"]));
            var conversation = new Conversation(owner, "Task conversation");
            seed.Conversations.Add(conversation);
            var task = new JarvisTask(owner, "Book dentist", "Book the dentist");
            task.AttachConversation(conversation.Id);
            task.Complete("Booked.");
            seed.Tasks.Add(task);
            seed.Memories.AddRange(
                Memory(owner, "Prefers green tea", "chat"),
                Memory(owner, "Journal mirror", "journal"),
                Memory(other, "Not mine", "chat"));
            await seed.SaveChangesAsync();
        }

        await using var database = CreateDbContext();
        var reviews = new WeeklyReviewRepository(database);
        var facts = await reviews.CollectAsync(owner, week, "UTC", CancellationToken.None);

        Assert.Equal(2, facts.Stats.JournalEntries);
        Assert.Equal(4.5, facts.Stats.Mood);
        Assert.Equal(2.0, facts.Stats.PreviousMood);
        Assert.Equal(week.AddDays(2), facts.Stats.BestDay);
        Assert.Equal(1, facts.Stats.TasksCompleted);
        Assert.Equal(["Book dentist"], facts.CompletedTasks);
        Assert.Equal(1, facts.Stats.NewMemories);
        Assert.Equal(["run", "family"], facts.Stats.TopTags);

        var delivered = await reviews.SaveAsync(owner, facts, "Good week.", narrated: true, notify: true,
            CancellationToken.None);
        Assert.NotNull(delivered?.NotifiedAt);
        Assert.Null(await reviews.SaveAsync(owner, facts, "Again.", true, notify: true, CancellationToken.None));
        var rewritten = await reviews.SaveAsync(owner, facts, "Rewritten.", false, notify: false,
            CancellationToken.None);
        Assert.Equal(delivered!.Id, rewritten!.Id);
        Assert.NotNull(rewritten.NotifiedAt);
        Assert.Equal(4.5, rewritten.Stats.Mood);

        Assert.Equal(week, await reviews.LastNotifiedWeekAsync(owner, CancellationToken.None));
        Assert.Null(await reviews.LastNotifiedWeekAsync(other, CancellationToken.None));
        Assert.Empty(await reviews.ListAsync(other, 10, CancellationToken.None));
        Assert.Null(await reviews.GetAsync(other, delivered.Id, CancellationToken.None));
        var notification = await database.Notifications.AsNoTracking().SingleAsync(x => x.OwnerId == owner);
        Assert.Equal("briefing.weekly", notification.Type);
        Assert.Equal(delivered.Id, notification.SourceId);
        Assert.Equal(2, await database.AuditEvents.CountAsync(x => x.OwnerId == owner && x.Tool == "briefings"));

        var trend = await reviews.TrendAsync(owner, week, 3, CancellationToken.None);
        Assert.Equal([week.AddDays(-14), week.AddDays(-7), week], trend.Select(point => point.WeekStart));
        Assert.Equal(2.0, trend[1].Mood);
        Assert.Equal(4.5, trend[2].Mood);
    }

    private static JournalEntryEntity Entry(Guid owner, DateOnly date, int mood, int rating, string[] tags) => new()
    {
        Id = Guid.CreateVersion7(), OwnerId = owner, EntryDate = date, Content = "Day.", Mood = mood,
        Rating = rating, Highlights = "Walked.", Tags = tags, CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static MemoryEntity Memory(Guid owner, string content, string source) => new()
    {
        Id = Guid.CreateVersion7(), OwnerId = owner, Content = content, SourceType = source, Importance = .5f,
        Confidence = .9f, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
