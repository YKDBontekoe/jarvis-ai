using Jarvis.Application.Browser;
using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

/// <summary>The one computer-use sandbox is held by at most one conversation at a time.</summary>
public sealed class ComputerSessionLeaseTests : IAsyncLifetime
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_busy_sandbox_refuses_other_conversations_until_it_is_released_or_idle()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var first = new Conversation(owner, "Book a table");
        var second = new Conversation(other, "Compare prices");
        await using (var seed = CreateDbContext())
        {
            seed.Conversations.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        await using var database = CreateDbContext();
        var sessions = new BrowserSessionRepository(database);
        var held = await sessions.TryStartComputerAsync(owner, first.Id, "Book", null, Idle, CancellationToken.None);
        Assert.NotNull(held);
        Assert.Equal(BrowserSessionKinds.Computer, held.Kind);
        Assert.Null(await sessions.TryStartComputerAsync(other, second.Id, "Compare", null, Idle,
            CancellationToken.None));

        // The same conversation may start over; its previous session is superseded.
        var again = await sessions.TryStartComputerAsync(owner, first.Id, "Book again", null, Idle,
            CancellationToken.None);
        Assert.NotNull(again);
        Assert.Equal("superseded", (await sessions.GetAsync(owner, held.Id, CancellationToken.None))!.Status);

        // Taking over, then a screenshot step, owner-scoped.
        Assert.False(await sessions.SetControlModeAsync(other, again.Id, ComputerControlModes.User,
            CancellationToken.None));
        Assert.True(await sessions.SetControlModeAsync(owner, again.Id, ComputerControlModes.User,
            CancellationToken.None));
        var step = await sessions.RecordStepAsync(again.Id, "computer_click", "Clicked.", true,
            CancellationToken.None, "computer-screenshots/a/b/c.jpg");
        Assert.Equal(1, step!.Ordinal);
        var reloaded = await sessions.GetAsync(owner, again.Id, CancellationToken.None);
        Assert.Equal(ComputerControlModes.User, reloaded!.ControlMode);
        Assert.Equal("computer-screenshots/a/b/c.jpg", reloaded.Steps.Single().ScreenshotKey);

        // An idle session no longer blocks the sandbox.
        await using (var age = CreateDbContext())
        {
            await age.BrowserSessions.Where(x => x.Id == again.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.UpdatedAt, DateTimeOffset.UtcNow.AddHours(-1)));
        }

        await using var later = CreateDbContext();
        var taken = await new BrowserSessionRepository(later).TryStartComputerAsync(other, second.Id, "Compare",
            null, Idle, CancellationToken.None);
        Assert.NotNull(taken);
        Assert.Equal("expired", (await new BrowserSessionRepository(later).GetAsync(owner, again.Id,
            CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Screenshots_beyond_the_kept_count_are_forgotten_oldest_first()
    {
        var owner = Guid.CreateVersion7();
        var conversation = new Conversation(owner, "Shop");
        await using (var seed = CreateDbContext())
        {
            seed.Conversations.Add(conversation);
            await seed.SaveChangesAsync();
        }

        await using var database = CreateDbContext();
        var sessions = new BrowserSessionRepository(database);
        var session = await sessions.TryStartComputerAsync(owner, conversation.Id, "Shop", null, Idle,
            CancellationToken.None);
        for (var i = 1; i <= 4; i++)
            await sessions.RecordStepAsync(session!.Id, "computer_screenshot", "Shot.", true,
                CancellationToken.None, $"computer-screenshots/{i}.jpg");

        var dropped = await sessions.TrimScreenshotsAsync(session!.Id, keep: 2, CancellationToken.None);

        Assert.Equal(["computer-screenshots/2.jpg", "computer-screenshots/1.jpg"], dropped);
        var kept = (await sessions.GetAsync(owner, session.Id, CancellationToken.None))!.Steps
            .Select(step => step.ScreenshotKey).ToArray();
        Assert.Equal(new string?[] { null, null, "computer-screenshots/3.jpg", "computer-screenshots/4.jpg" }, kept);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
