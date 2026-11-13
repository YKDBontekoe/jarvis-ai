using Jarvis.Application.Knowing;
using Jarvis.Domain.Knowing;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class KnowingPersistenceTests : IAsyncLifetime
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
    public async Task Reviews_and_chat_modes_are_owner_scoped()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var conversation = Guid.CreateVersion7();
        await using (var database = CreateDbContext())
        {
            var repository = new KnowingRepository(database);
            await repository.AddReviewAsync(new MemoryReview(Guid.CreateVersion7(), owner, Guid.NewGuid(),
                MemoryVerdicts.Right, "Runs before work", now.AddMinutes(-1)), default);
            await repository.AddReviewAsync(new MemoryReview(Guid.CreateVersion7(), owner, Guid.NewGuid(),
                MemoryVerdicts.Wrong, "Has a cat", now), default);
            await repository.AddChatModeAsync(new ChatMode(conversation, owner, ChatModeKinds.Council,
                """{"question":"Berlin?","advisers":["A","B"]}""", now), default);
        }

        await using (var database = CreateDbContext())
        {
            var repository = new KnowingRepository(database);
            Assert.Equal([MemoryVerdicts.Right, MemoryVerdicts.Wrong],
                (await repository.ListReviewsAsync(owner, default)).Select(r => r.Verdict));
            Assert.Empty(await repository.ListReviewsAsync(other, default));
            var mode = await repository.GetChatModeAsync(conversation, owner, default);
            Assert.Equal(ChatModeKinds.Council, mode!.Kind);
            Assert.Contains("Berlin?", mode.PayloadJson);
            Assert.Null(await repository.GetChatModeAsync(conversation, other, default));
            Assert.Single(await repository.ListChatModesAsync(owner, default));
        }
    }

    [Fact]
    public async Task Approval_history_reads_only_the_owners_decided_approvals_oldest_first()
    {
        var owner = Guid.CreateVersion7();
        await using (var database = CreateDbContext())
        {
            var conversation = new Jarvis.Domain.Conversations.Conversation(owner, "Chat");
            var args = "{}";
            database.Conversations.Add(conversation);
            await database.SaveChangesAsync();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                insert into tool_approvals ("Id", "OwnerId", "ConversationId", "RequestId", "ToolCallId", "ToolName",
                  "ArgumentsJson", "Status", "Approved", "CreatedAt", "DecidedAt", "ResumeStatus") values
                ({Guid.NewGuid()}, {owner}, {conversation.Id}, 'r1', 'c1', 'DeleteExpense', {args}::jsonb, 'decided', true,
                  now() - interval '2 hours', now() - interval '2 hours', 'completed'),
                ({Guid.NewGuid()}, {owner}, {conversation.Id}, 'r2', 'c2', 'DeleteExpense', {args}::jsonb, 'decided', false,
                  now() - interval '1 hour', now() - interval '1 hour', 'completed'),
                ({Guid.NewGuid()}, {owner}, {conversation.Id}, 'r3', 'c3', 'DeleteExpense', {args}::jsonb, 'pending', null,
                  now(), null, 'not_started')
                """);
        }

        await using (var database = CreateDbContext())
        {
            var decided = await new ApprovalHistory(database)
                .ListDecidedAsync(owner, DateTimeOffset.UtcNow.AddDays(-1), 100, default);
            Assert.Equal([true, false], decided.Select(d => d.Approved));
            Assert.Empty(await new ApprovalHistory(database)
                .ListDecidedAsync(Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddDays(-1), 100, default));
        }
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
