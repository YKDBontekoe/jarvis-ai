using Jarvis.Application.Learning;
using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class TurnTraceUsageTests : IAsyncLifetime
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
    public async Task A_trace_keeps_its_model_call_and_token_totals()
    {
        var owner = Guid.CreateVersion7();
        Guid conversation;
        await using (var database = CreateDbContext())
        {
            var chat = new Conversation(owner, "Usage");
            database.Conversations.Add(chat);
            await database.SaveChangesAsync();
            conversation = chat.Id;
            var store = new LearningStore(database);
            await store.AddTraceAsync(Record(owner, conversation, new TurnUsage(3, 9_000, 6_000, 400)), default);
            await store.AddTraceAsync(Record(owner, conversation, null), default);
            await store.AddTraceAsync(Record(owner, conversation, new TurnUsage(0, 0, 0, 0)), default);
        }

        await using var read = CreateDbContext();
        var traces = await new LearningStore(read).ListTracesAsync(owner, DateTimeOffset.UtcNow.AddDays(-1), 10, default);

        Assert.Equal(3, traces.Count);
        var counted = Assert.Single(traces, trace => trace.Usage is not null);
        Assert.Equal(new TurnUsage(3, 9_000, 6_000, 400), counted.Usage);
        Assert.Equal(2.0 / 3.0, counted.Usage!.CacheHitRatio!.Value, 3);
    }

    private static TurnTraceRecord Record(Guid owner, Guid conversation, TurnUsage? usage) =>
        new(Guid.CreateVersion7(), owner, conversation, null, null, TurnKinds.Interactive, [], [],
            [new TurnToolCall("SearchMemory", ToolOutcomes.Completed, 50)], 1_200, "completed",
            DateTimeOffset.UtcNow, usage);

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
