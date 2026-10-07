using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Files;
using Jarvis.Domain.Profiles;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class JarvisDbContextModelTests
{
    private static IModel Model
    {
        get
        {
            var options = new DbContextOptionsBuilder<JarvisDbContext>()
                .UseNpgsql("Host=localhost;Database=jarvis_model_test;Username=jarvis;Password=jarvis",
                    npgsql => npgsql.UseVector())
                .Options;
            using var context = new JarvisDbContext(options);
            return context.Model;
        }
    }

    [Fact]
    public void Conversation_has_owner_scoped_list_index()
    {
        var entity = Model.FindEntityType(typeof(Conversation))!;
        Assert.Contains(entity.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId", "UpdatedAt" }));
    }

    [Fact]
    public void ToolApproval_has_idempotency_unique_index()
    {
        var entity = Model.FindEntityType(typeof(ToolApproval))!;
        var index = entity.GetIndexes().Single(i => i.GetDatabaseName() == "ux_tool_approvals_idempotency");
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "OwnerId", "RequestId", "ToolCallId" },
            index.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void ToolApproval_task_foreign_key_uses_set_null_on_delete()
    {
        var entity = Model.FindEntityType(typeof(ToolApproval))!;
        var taskFk = entity.GetForeignKeys().Single(fk => fk.Properties.Single().Name == "TaskId");
        Assert.Equal(DeleteBehavior.SetNull, taskFk.DeleteBehavior);
        var conversationFk = entity.GetForeignKeys().Single(fk => fk.Properties.Single().Name == "ConversationId");
        Assert.Equal(DeleteBehavior.Cascade, conversationFk.DeleteBehavior);
    }

    [Fact]
    public void AuditEvent_has_owner_timestamp_index()
    {
        var entity = Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Contains(entity.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId", "Timestamp" }));
    }

    [Fact]
    public void MemoryEntity_has_vector_and_trigram_indexes()
    {
        var entity = Model.FindEntityType(typeof(MemoryEntity))!;
        Assert.Contains(entity.GetIndexes(),
            index => index.Properties.Count == 1
                && index.Properties[0].Name == "Embedding"
                && index.GetMethod() == "hnsw");

        Assert.Contains(entity.GetIndexes(),
            index => index.Properties.Count == 1
                && index.Properties[0].Name == "Content"
                && index.GetMethod() == "gin");
    }

    [Fact]
    public void FileContentChunkEntity_has_hnsw_embedding_index()
    {
        var entity = Model.FindEntityType(typeof(FileContentChunkEntity))!;
        Assert.Contains(entity.GetIndexes(),
            index => index.Properties.Count == 1
                && index.Properties[0].Name == "Embedding"
                && index.GetMethod() == "hnsw");
    }

    [Fact]
    public void Memory_entity_uses_pgvector_column_type_for_embeddings()
    {
        var property = Model.FindEntityType(typeof(MemoryEntity))!.FindProperty(nameof(MemoryEntity.Embedding))!;
        Assert.Equal("vector(1536)", property.GetColumnType());
    }

    [Fact]
    public void Assistant_profile_has_one_default_per_owner()
    {
        var entity = Model.FindEntityType(typeof(AssistantProfile))!;
        var index = entity.GetIndexes().Single(item => item.GetDatabaseName() == "ux_assistant_profiles_default");
        Assert.True(index.IsUnique);
        Assert.Equal(["OwnerId"], index.Properties.Select(property => property.Name).ToArray());
        Assert.Equal("is_default = TRUE", index.GetFilter());
    }

    [Fact]
    public void Conversation_stores_a_profile_snapshot()
    {
        var entity = Model.FindEntityType(typeof(Conversation))!;
        Assert.NotNull(entity.FindProperty(nameof(Conversation.ProfileSnapshotJson)));
        Assert.Contains(entity.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual(["OwnerId", "ProfileId"]));
    }

    [Fact]
    public void Document_collections_are_unique_per_owner_name()
    {
        var entity = Model.FindEntityType(typeof(DocumentCollection))!;
        Assert.Contains(entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["OwnerId", "Name"]));
    }

    [Fact]
    public void SaveChanges_rejects_audit_event_mutation()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql("Host=invalid.example;Database=jarvis;Username=jarvis;Password=jarvis",
                npgsql => npgsql.UseVector())
            .Options;
        using var context = new JarvisDbContext(options);
        var audit = new AuditEvent(Guid.NewGuid(), "tool", "action", "low", true);
        context.AuditEvents.Attach(audit);
        context.Entry(audit).State = EntityState.Modified;
        var modified = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("append-only", modified.Message, StringComparison.OrdinalIgnoreCase);

        context.Entry(audit).State = EntityState.Deleted;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Reminder_and_automation_conversation_links_are_unique()
    {
        var reminder = Model.FindEntityType(typeof(Jarvis.Domain.Workflows.Reminder))!;
        Assert.Contains(reminder.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["ConversationId"])
                && index.GetFilter() == "conversation_id IS NOT NULL");
        var reminderFk = reminder.GetForeignKeys().Single(fk => fk.Properties.Single().Name == "ConversationId");
        Assert.Equal(DeleteBehavior.SetNull, reminderFk.DeleteBehavior);

        var automation = Model.FindEntityType(typeof(Jarvis.Domain.Automations.AutomationRule))!;
        Assert.Contains(automation.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["ConversationId"])
                && index.GetFilter() == "conversation_id IS NOT NULL");
        var automationFk = automation.GetForeignKeys().Single(fk => fk.Properties.Single().Name == "ConversationId");
        Assert.Equal(DeleteBehavior.SetNull, automationFk.DeleteBehavior);
    }

    [Fact]
    public void Learning_signals_have_no_foreign_key_to_messages_so_regenerate_cannot_erase_them()
    {
        var signal = Model.FindEntityType(typeof(LearningSignalEntity))!;
        Assert.Empty(signal.GetForeignKeys());
        Assert.Equal("learning_signals", signal.GetTableName());
        Assert.Contains(signal.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId", "CreatedAt" }));
        Assert.Contains(signal.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId", "Kind", "CreatedAt" }));
    }

    [Fact]
    public void Turn_traces_are_owner_scoped_by_time_and_by_reply_and_keep_their_lists_as_jsonb()
    {
        var trace = Model.FindEntityType(typeof(TurnTraceEntity))!;
        Assert.Empty(trace.GetForeignKeys());
        Assert.Contains(trace.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId", "CreatedAt" }));
        Assert.Contains(trace.GetIndexes(),
            index => index.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId", "MessageId" }));
        foreach (var column in new[] { "MemoryIdsJson", "SkillsJson", "ToolsJson" })
            Assert.Equal("jsonb", trace.FindProperty(column)!.GetColumnType());
    }
}
