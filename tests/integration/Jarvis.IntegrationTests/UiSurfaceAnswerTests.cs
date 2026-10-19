using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class UiSurfaceAnswerTests : IAsyncLifetime
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
    public async Task A_used_card_keeps_its_answer_until_it_is_reopened()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var chat = new Conversation(owner, "Gym");
        await using (var seed = CreateDbContext())
        {
            seed.Conversations.Add(chat);
            await seed.SaveChangesAsync();
        }

        await using var database = CreateDbContext();
        var surfaces = new UiSurfaceRepository(database);
        var created = await surfaces.CreateAsync(owner, chat.Id, "choice", "Which tone?", "{}",
            CancellationToken.None);
        await surfaces.CompleteAsync(owner, created.Surface.Id, "hard", """{"choice":"hard","label":"Hard push"}""",
            CancellationToken.None);

        var listed = Assert.Single(await surfaces.ListForConversationAsync(owner, chat.Id, CancellationToken.None));
        Assert.Equal("completed", listed.Status);
        Assert.Equal("hard", listed.CompletedAction);
        Assert.Contains("Hard push", listed.ValuesJson);
        Assert.Empty(await surfaces.ListForConversationAsync(other, chat.Id, CancellationToken.None));
        Assert.Null(await surfaces.GetAsync(other, created.Surface.Id, CancellationToken.None));

        await surfaces.ReopenAsync(owner, created.Surface.Id, CancellationToken.None);
        var reopened = await surfaces.GetAsync(owner, created.Surface.Id, CancellationToken.None);
        Assert.Equal("open", reopened!.Status);
        Assert.Null(reopened.CompletedAction);
        Assert.Null(reopened.ValuesJson);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
