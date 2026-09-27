using Jarvis.Infrastructure.Persistence;
using Jarvis.Application.Files;
using Jarvis.Application.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Files;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using System.Text.Json;

namespace Jarvis.IntegrationTests;

public sealed class ConversationOwnershipTests : IAsyncLifetime
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
    public async Task Conversation_reads_and_lists_are_scoped_to_the_owner()
    {
        var owner = Guid.CreateVersion7();
        var otherOwner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var conversation = await store.CreateAsync(owner, "Private conversation", CancellationToken.None);

        Assert.NotNull(await store.GetAsync(conversation.Id, owner, CancellationToken.None));
        Assert.Null(await store.GetAsync(conversation.Id, otherOwner, CancellationToken.None));
        Assert.Single(await store.ListAsync(owner, CancellationToken.None));
        Assert.Empty(await store.ListAsync(otherOwner, CancellationToken.None));
    }

    [Fact]
    public async Task Memory_search_supports_null_category_and_excludes_other_owners_and_expired_records()
    {
        var owner = Guid.CreateVersion7();
        var otherOwner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var memories = new MemoryRepository(database);
        const string content = "Meeting notes use concise bullets and decisions with action owners.";
        var current = await memories.CreateAsync(owner, "preference", content, 0.8f, 1,
            null, null, null, false, CancellationToken.None);
        await memories.CreateAsync(otherOwner, "preference", content, 0.8f, 1,
            null, null, null, false, CancellationToken.None);
        await memories.CreateAsync(owner, "preference", content, 0.8f, 1,
            null, null, DateTimeOffset.UtcNow.AddDays(-1), false, CancellationToken.None);

        foreach (var kind in new string?[] { null, "preference" })
        {
            var text = await memories.SearchTextAsync(owner, "meeting notes", kind, CancellationToken.None);
            Assert.Equal(current.Id, Assert.Single(text).Id);
            var fuzzy = await memories.SearchTrigramAsync(owner, content, kind, CancellationToken.None);
            Assert.Equal(current.Id, Assert.Single(fuzzy).Id);
        }
        Assert.Empty(await memories.SearchTextAsync(owner, "meeting notes", "fact", CancellationToken.None));
        Assert.Empty(await memories.SearchTrigramAsync(owner, content, "fact", CancellationToken.None));
    }

    [Fact]
    public async Task Persisted_agent_session_restores_polymorphic_metadata_after_jsonb_roundtrip()
    {
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var conversation = await store.CreateAsync(Guid.CreateVersion7(), "Session verification", CancellationToken.None);
        const string state = """{"stateBag":{"messages":[{"contents":[{"$id":"1","$type":"functionCall","name":"AddMcpServer","callId":"approval-1","arguments":{"name":"Verification"}}]}]}}""";
        await store.SaveAgentSessionAsync(conversation.Id, state, CancellationToken.None);
        database.ChangeTracker.Clear();

        using var restored = JsonDocument.Parse((await store.GetAgentSessionAsync(conversation.Id, CancellationToken.None))!);
        var call = restored.RootElement.GetProperty("stateBag").GetProperty("messages")[0].GetProperty("contents")[0];
        Assert.Equal("$type", call.EnumerateObject().First().Name);
        Assert.Equal("approval-1", call.GetProperty("callId").GetString());
        Assert.Equal("Verification", call.GetProperty("arguments").GetProperty("name").GetString());
    }

    [Fact]
    public async Task File_search_joins_stored_files_and_returns_only_ready_files_owned_by_the_user()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var files = new FileRepository(database);
        var contents = new FileContentRepository(database);
        var expectedId = Guid.CreateVersion7();
        foreach (var (id, fileOwner, status) in new[]
        {
            (expectedId, owner, "ready"),
            (Guid.CreateVersion7(), Guid.CreateVersion7(), "ready"),
            (Guid.CreateVersion7(), owner, "queued")
        })
        {
            await files.CreateAsync(new StoredFile(id, fileOwner, id.ToString(), "invoice.txt", "text/plain",
                32, new string('0', 64), DateTimeOffset.UtcNow, status), CancellationToken.None);
            await contents.ReplaceChunksAsync(id, fileOwner,
                [new FileContentChunk(id, fileOwner, 0, "Verification invoice renewal terms.")], CancellationToken.None);
        }

        var hits = await contents.SearchTextAsync(owner, "invoice", CancellationToken.None);
        var hit = Assert.Single(hits);
        Assert.Equal(expectedId, hit.FileId);
        Assert.Equal("invoice.txt", hit.FileName);
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_its_approval_notifications()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var conversation = await store.CreateAsync(owner, "Needs approval", CancellationToken.None);
        var approval = new ToolApproval(owner, conversation.Id, "req-1", "call-1", "ForgetMemory", "{}");
        var approvalNotification = new Notification(Guid.CreateVersion7(), owner, "approval.required",
            "Approval needed", "ForgetMemory", approval.Id);
        var reminderNotification = new Notification(Guid.CreateVersion7(), owner, "reminder.fired",
            "Reminder", "Standup", Guid.CreateVersion7());
        database.ToolApprovals.Add(approval);
        database.Notifications.Add(approvalNotification);
        database.Notifications.Add(reminderNotification);
        await database.SaveChangesAsync();

        Assert.Equal(ConversationDeleteResult.Deleted,
            await store.DeleteAsync(conversation.Id, owner, CancellationToken.None));
        database.ChangeTracker.Clear();

        Assert.Empty(await database.Notifications.Where(x => x.Id == approvalNotification.Id).ToListAsync());
        Assert.Single(await database.Notifications.Where(x => x.Id == reminderNotification.Id).ToListAsync());
        Assert.Empty(await database.ToolApprovals.Where(x => x.Id == approval.Id).ToListAsync());
        Assert.Single(await database.AuditEvents.Where(x =>
            x.OwnerId == owner && x.Action == "conversation.deleted").ToListAsync());
    }

    [Fact]
    public async Task Terminal_file_status_does_not_overwrite_a_requeued_file()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var files = new FileRepository(database);
        var id = Guid.CreateVersion7();
        await files.CreateAsync(new StoredFile(id, owner, id.ToString(), "notes.txt", "text/plain",
            32, new string('0', 64), DateTimeOffset.UtcNow, "queued"), CancellationToken.None);

        Assert.True(await files.SetProcessingStatusAsync(id, owner, "processing", CancellationToken.None));
        Assert.True(await files.RequeueForProcessingAsync(id, owner, CancellationToken.None));
        Assert.False(await files.SetProcessingStatusAsync(id, owner, "failed", CancellationToken.None));
        Assert.Equal("queued", (await files.GetAsync(id, owner, CancellationToken.None))!.ProcessingStatus);

        Assert.True(await files.SetProcessingStatusAsync(id, owner, "processing", CancellationToken.None));
        Assert.True(await files.SetProcessingStatusAsync(id, owner, "ready", CancellationToken.None));
        Assert.Equal("ready", (await files.GetAsync(id, owner, CancellationToken.None))!.ProcessingStatus);
    }

    [Fact]
    public async Task Cancelling_incomplete_task_approvals_removes_their_inbox_and_push()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversations = new ConversationStore(database);
        var tasks = new WorkflowRepository(database);
        var approvals = new ToolApprovalStore(database);
        var conversation = await conversations.CreateAsync(owner, "Task chat", CancellationToken.None);
        var task = await tasks.CreateAsync(owner, "Research", "Look this up", conversation.Id, CancellationToken.None);
        database.PushDevices.Add(new PushDevice(owner, "fcm-token", "android"));
        await database.SaveChangesAsync();

        var created = await approvals.CreateAsync(owner, conversation.Id, "req-1", "call-1",
            "ForgetMemory", "{}", task.Id, CancellationToken.None);
        Assert.True(created.Created);
        Assert.NotNull(created.NotificationId);
        Assert.NotEmpty(await database.PushDeliveries.Where(x => x.NotificationId == created.NotificationId)
            .ToListAsync());

        await approvals.CancelIncompleteForTaskAsync(task.Id, owner, CancellationToken.None);
        database.ChangeTracker.Clear();

        Assert.Empty(await database.Notifications.Where(x => x.Id == created.NotificationId).ToListAsync());
        Assert.Empty(await database.PushDeliveries.Where(x => x.NotificationId == created.NotificationId)
            .ToListAsync());
        Assert.Equal("cancelled",
            (await database.ToolApprovals.SingleAsync(x => x.Id == created.Approval.Id)).Status);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
