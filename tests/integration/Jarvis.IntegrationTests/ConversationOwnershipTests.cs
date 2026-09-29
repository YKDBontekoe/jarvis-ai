using Jarvis.Infrastructure.Persistence;
using Jarvis.Application.Files;
using Jarvis.Application.Conversations;
using Jarvis.Domain.Conversations;
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
    public async Task Message_pages_are_bounded_stable_and_deterministic_for_equal_timestamps()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var conversation = await store.CreateAsync(owner, "Long transcript", CancellationToken.None);
        var messages = Enumerable.Range(0, 205)
            .Select(index => new Message(conversation.Id, index % 2 == 0 ? "user" : "assistant", $"message-{index}"))
            .ToArray();
        database.Messages.AddRange(messages);
        await database.SaveChangesAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        await database.Messages.Where(message => message.ConversationId == conversation.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(message => message.CreatedAt, timestamp));
        database.ChangeTracker.Clear();

        var received = new List<Message>();
        MessageCursor? cursor = null;
        do
        {
            var page = await store.GetMessagePageAsync(conversation.Id, cursor, 37, CancellationToken.None);
            Assert.InRange(page.Items.Count, 1, 37);
            received.InsertRange(0, page.Items);
            cursor = page.NextCursor;
            if (!page.HasMore) break;
            Assert.NotNull(cursor);
        } while (true);

        Assert.Equal(205, received.Count);
        Assert.Equal(205, received.Select(message => message.Id).Distinct().Count());
        Assert.Equal(received.OrderBy(message => message.CreatedAt).ThenBy(message => message.Id), received);
    }

    [Fact]
    public async Task Message_cursor_remains_valid_after_boundary_deletion_and_concurrent_insert()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var conversation = await store.CreateAsync(owner, "Changing transcript", CancellationToken.None);
        for (var index = 0; index < 5; index++)
            await store.AddMessageAsync(new Message(conversation.Id, "user", index.ToString()), CancellationToken.None);

        var newest = await store.GetMessagePageAsync(conversation.Id, null, 2, CancellationToken.None);
        var boundary = Assert.IsType<MessageCursor>(newest.NextCursor);
        database.Messages.Remove(await database.Messages.SingleAsync(message => message.Id == boundary.Id));
        database.Messages.Add(new Message(conversation.Id, "assistant", "concurrent"));
        await database.SaveChangesAsync();

        var older = await store.GetMessagePageAsync(conversation.Id, boundary, 10, CancellationToken.None);
        Assert.False(older.HasMore);
        Assert.Equal(3, older.Items.Count);
        Assert.All(older.Items, message => Assert.True(message.CreatedAt < boundary.CreatedAt ||
            message.CreatedAt == boundary.CreatedAt && message.Id.CompareTo(boundary.Id) < 0));
        Assert.Empty((await store.GetMessagePageAsync(Guid.CreateVersion7(), null, 10, CancellationToken.None)).Items);
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

    [Fact]
    public async Task Cancelling_a_task_clears_decided_incomplete_approval_inbox()
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

        var created = await approvals.CreateAsync(owner, conversation.Id, "req-2", "call-2",
            "ForgetMemory", "{}", task.Id, CancellationToken.None);
        Assert.True(created.Created);
        Assert.NotNull(created.NotificationId);
        Assert.NotNull(await approvals.DecideAsync(created.Approval.Id, owner, true, CancellationToken.None));

        Assert.True(await tasks.CancelTaskAsync(task.Id, owner, CancellationToken.None));
        database.ChangeTracker.Clear();

        Assert.Empty(await database.Notifications.Where(x => x.Id == created.NotificationId).ToListAsync());
        Assert.Empty(await database.PushDeliveries.Where(x => x.NotificationId == created.NotificationId)
            .ToListAsync());
        var stored = await database.ToolApprovals.SingleAsync(x => x.Id == created.Approval.Id);
        Assert.Equal("approved", stored.Status);
        Assert.Equal("cancelled", stored.ResumeStatus);
    }

    [Fact]
    public async Task Completing_a_resume_removes_its_inbox_and_keeps_other_pending()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversations = new ConversationStore(database);
        var approvals = new ToolApprovalStore(database);
        var conversation = await conversations.CreateAsync(owner, "Chat", CancellationToken.None);
        database.PushDevices.Add(new PushDevice(owner, "fcm-token", "android"));
        await database.SaveChangesAsync();

        var completed = await approvals.CreateAsync(owner, conversation.Id, "req-1", "call-1",
            "CreateReminder", "{}", null, CancellationToken.None);
        var pending = await approvals.CreateAsync(owner, conversation.Id, "req-2", "call-2",
            "ForgetMemory", "{}", null, CancellationToken.None);
        Assert.True(completed.Created);
        Assert.NotNull(completed.NotificationId);
        Assert.NotNull(pending.NotificationId);

        Assert.NotNull(await approvals.DecideAsync(completed.Approval.Id, owner, true, CancellationToken.None));
        Assert.True(await approvals.TryStartResumeAsync(completed.Approval.Id, owner, CancellationToken.None));
        await approvals.MarkResumeCompletedAsync(completed.Approval.Id, owner, CancellationToken.None);
        database.ChangeTracker.Clear();

        Assert.Empty(await database.Notifications.Where(x => x.Id == completed.NotificationId).ToListAsync());
        Assert.Empty(await database.PushDeliveries.Where(x => x.NotificationId == completed.NotificationId)
            .ToListAsync());
        Assert.Single(await database.Notifications.Where(x => x.Id == pending.NotificationId).ToListAsync());
        Assert.NotEmpty(await database.PushDeliveries.Where(x => x.NotificationId == pending.NotificationId)
            .ToListAsync());
        Assert.Equal("completed",
            (await database.ToolApprovals.SingleAsync(x => x.Id == completed.Approval.Id)).ResumeStatus);
    }

    [Fact]
    public async Task Failed_resume_keeps_inbox_for_retry()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversations = new ConversationStore(database);
        var approvals = new ToolApprovalStore(database);
        var conversation = await conversations.CreateAsync(owner, "Chat", CancellationToken.None);
        database.PushDevices.Add(new PushDevice(owner, "fcm-token", "android"));
        await database.SaveChangesAsync();

        var created = await approvals.CreateAsync(owner, conversation.Id, "req-1", "call-1",
            "CreateReminder", "{}", null, CancellationToken.None);
        Assert.NotNull(created.NotificationId);
        Assert.NotNull(await approvals.DecideAsync(created.Approval.Id, owner, true, CancellationToken.None));
        Assert.True(await approvals.TryStartResumeAsync(created.Approval.Id, owner, CancellationToken.None));
        await approvals.MarkResumeFailedAsync(created.Approval.Id, owner, CancellationToken.None);
        database.ChangeTracker.Clear();

        Assert.Single(await database.Notifications.Where(x => x.Id == created.NotificationId).ToListAsync());
        Assert.NotEmpty(await database.PushDeliveries.Where(x => x.NotificationId == created.NotificationId)
            .ToListAsync());
        Assert.Equal("failed",
            (await database.ToolApprovals.SingleAsync(x => x.Id == created.Approval.Id)).ResumeStatus);
    }

    [Fact]
    public async Task Conversation_file_attachments_reject_cross_owner_files()
    {
        var owner = Guid.CreateVersion7();
        var otherOwner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversations = new ConversationStore(database);
        var files = new FileRepository(database);
        var context = new ConversationFileContextRepository(database);
        var conversation = await conversations.CreateAsync(owner, "Sources", CancellationToken.None);
        var foreignId = Guid.CreateVersion7();
        await files.CreateAsync(new StoredFile(foreignId, otherOwner, foreignId.ToString(), "secret.txt", "text/plain",
            12, new string('0', 64), DateTimeOffset.UtcNow, "ready"), CancellationToken.None);

        Assert.False(await context.AttachFileAsync(conversation.Id, foreignId, owner, CancellationToken.None));
    }

    [Fact]
    public async Task Scoped_file_search_limits_results_to_attached_files()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversations = new ConversationStore(database);
        var files = new FileRepository(database);
        var contents = new FileContentRepository(database);
        var context = new ConversationFileContextRepository(database);
        var conversation = await conversations.CreateAsync(owner, "Scoped", CancellationToken.None);
        var attachedId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        foreach (var (id, phrase) in new[] { (attachedId, "alpha project scope"), (otherId, "alpha unrelated") })
        {
            await files.CreateAsync(new StoredFile(id, owner, id.ToString(), $"{id}.txt", "text/plain",
                32, new string('0', 64), DateTimeOffset.UtcNow, "ready"), CancellationToken.None);
            await contents.ReplaceChunksAsync(id, owner,
                [new FileContentChunk(id, owner, 0, phrase)], CancellationToken.None);
        }

        Assert.True(await context.AttachFileAsync(conversation.Id, attachedId, owner, CancellationToken.None));
        var scopedIds = await context.ResolveScopedFileIdsAsync(conversation.Id, owner, CancellationToken.None);
        var hits = await contents.SearchTextAsync(owner, "alpha", CancellationToken.None, scopedIds);
        var hit = Assert.Single(hits);
        Assert.Equal(attachedId, hit.FileId);
    }

    [Fact]
    public async Task Document_collection_names_are_unique_per_owner()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var collections = new DocumentCollectionRepository(database);
        await collections.CreateAsync(owner, new DocumentCollectionDraft("Project Docs", null), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            collections.CreateAsync(owner, new DocumentCollectionDraft("Project Docs", null), CancellationToken.None));
    }

    [Fact]
    public async Task Assistant_messages_persist_citations_json()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var conversations = new ConversationStore(database);
        var conversation = await conversations.CreateAsync(owner, "Citations", CancellationToken.None);
        var chunkId = Guid.CreateVersion7();
        var citations = JsonSerializer.Serialize(new[]
        {
            new FileCitation(Guid.CreateVersion7(), "notes.txt", chunkId, 0, "excerpt", 2)
        });
        var message = new Jarvis.Domain.Conversations.Message(conversation.Id, "assistant", "Answer with source.",
            citationsJson: citations);
        await conversations.AddMessageAsync(message, CancellationToken.None);
        database.ChangeTracker.Clear();

        var stored = Assert.Single(await conversations.GetMessagesAsync(conversation.Id, CancellationToken.None));
        Assert.Contains(chunkId.ToString(), stored.CitationsJson, StringComparison.Ordinal);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
