using Jarvis.Application.Improvements;
using Jarvis.Application.Learning;
using Jarvis.Domain.Improvements;
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

    [Fact]
    public async Task A_trace_keeps_the_lessons_that_were_shown_in_the_run()
    {
        var owner = Guid.CreateVersion7();
        var lessons = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        await using (var database = CreateDbContext())
        {
            var chat = new Conversation(owner, "Lessons");
            database.Conversations.Add(chat);
            await database.SaveChangesAsync();
            var store = new LearningStore(database);
            await store.AddTraceAsync(Record(owner, chat.Id, null, lessons), default);
            await store.AddTraceAsync(Record(owner, chat.Id, null), default);
        }

        await using var read = CreateDbContext();
        var traces = await new LearningStore(read).ListTracesAsync(owner, DateTimeOffset.UtcNow.AddDays(-1), 10, default);

        Assert.Equal(lessons, Assert.Single(traces, trace => trace.LessonIds.Count > 0).LessonIds);
        Assert.Single(traces, trace => trace.LessonIds.Count == 0);
    }

    [Fact]
    public async Task A_lesson_proposal_can_be_stored_and_read_back()
    {
        var owner = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var payload = System.Text.Json.JsonSerializer.Serialize(new LessonPayload("Check the inputs first.", ["SearchMail"]),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var proposal = new ImprovementProposal(Guid.CreateVersion7(), owner, ImprovementKinds.Lesson,
            LessonRules.Fingerprint("SearchMail", now), "Lesson: Check the inputs first.", "It went wrong twice.", 0.8,
            payload, ImprovementStatuses.Applied, null, now, now);
        await using (var database = CreateDbContext())
            await new ImprovementRepository(database).AddAsync(proposal with { ResultingRef = proposal.Id }, default);

        await using var read = CreateDbContext();
        var stored = Assert.Single(await new ImprovementRepository(read).ListAsync(owner, default));

        Assert.Equal(ImprovementKinds.Lesson, stored.Kind);
        Assert.Equal(proposal.Id, stored.ResultingRef);
        Assert.Contains("Check the inputs first.", stored.PayloadJson);
    }

    private static TurnTraceRecord Record(Guid owner, Guid conversation, TurnUsage? usage, Guid[]? lessons = null) =>
        new(Guid.CreateVersion7(), owner, conversation, null, null, TurnKinds.Interactive, [], [],
            [new TurnToolCall("SearchMemory", ToolOutcomes.Completed, 50)], 1_200, "completed",
            DateTimeOffset.UtcNow, usage, lessons);

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
