using Jarvis.Application.Learning;
using Jarvis.Application.Usage;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class UsageTurnQualityTests : IAsyncLifetime
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
    public async Task Turn_quality_reads_chat_calls_traces_and_approvals_for_this_owner_only()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await using (var database = CreateDbContext())
        {
            var chat = new Conversation(owner, "Quality");
            var theirs = new Conversation(other, "Theirs");
            database.Conversations.AddRange(chat, theirs);
            await database.SaveChangesAsync();
            var conversation = chat.Id;
            for (var index = 1; index <= 6; index++)
                database.ModelUsageEvents.Add(Usage(owner, UsagePurposes.Chat, index * 1_000, now.AddMinutes(-index)));
            database.ModelUsageEvents.Add(Usage(owner, UsagePurposes.Background, 99_000, now.AddMinutes(-1)));
            database.ModelUsageEvents.Add(Usage(other, UsagePurposes.Chat, 50_000, now.AddMinutes(-1)));
            database.TurnTraces.Add(Trace(owner, conversation, "completed", now.AddMinutes(-2),
                new TurnToolCall("SearchMemory", ToolOutcomes.Completed, 120),
                new TurnToolCall("BrowseTheWeb", ToolOutcomes.Failed, 4_000)));
            database.TurnTraces.Add(Trace(owner, conversation, "failed", now.AddMinutes(-3)));
            database.TurnTraces.Add(Trace(other, conversation, "failed", now.AddMinutes(-3)));
            var asked = new ToolApproval(owner, conversation, "r1", "c1", "ForgetMemory", "{}");
            var rejected = new ToolApproval(owner, conversation, "r2", "c2", "ForgetMemory", "{}");
            rejected.Decide(false);
            database.ToolApprovals.AddRange(asked, rejected,
                new ToolApproval(other, theirs.Id, "r3", "c3", "ForgetMemory", "{}"));
            database.AuditEvents.Add(new AuditEvent(owner, "SearchMemory", "approval.policy_auto_approved", "low", true));
            database.AuditEvents.Add(new AuditEvent(other, "SearchMemory", "approval.auto_approved", "low", true));
            await database.SaveChangesAsync();
        }

        await using var read = CreateDbContext();
        var quality = (await new UsageDashboardService(read).GetAsync(owner, UsagePeriods.SevenDays, default)).TurnQuality!;

        Assert.Equal(6, quality.ModelCalls);
        Assert.Equal(3_000, quality.MedianModelMs);
        Assert.Equal(0.25, quality.CacheHitRate);
        Assert.Equal(2, quality.Turns);
        Assert.Equal(1, quality.FailedTurns);
        Assert.Equal(2, quality.ToolCalls);
        Assert.Equal(0.5, quality.ToolFailureRate);
        Assert.Equal(2, quality.ApprovalsAsked);
        Assert.Equal(1, quality.ApprovalsDenied);
        Assert.Equal(1, quality.AutoApproved);
    }

    private static ModelUsageEventEntity Usage(Guid owner, string purpose, int ms, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = owner,
        Provider = UsageProviders.Codex,
        Purpose = purpose,
        Model = "test",
        InputTokens = 1_000,
        CachedInputTokens = 250,
        OutputTokens = 100,
        DurationMs = ms,
        Outcome = UsageOutcomes.Completed,
        CreatedAt = at
    };

    private static TurnTraceEntity Trace(Guid owner, Guid conversation, string outcome, DateTimeOffset at,
        params TurnToolCall[] tools) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = owner,
        ConversationId = conversation,
        Kind = TurnKinds.Interactive,
        ToolsJson = TurnTraceEntity.Write(tools),
        TotalMs = 5_000,
        Outcome = outcome,
        CreatedAt = at
    };

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
