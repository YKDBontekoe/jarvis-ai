using Jarvis.Domain.Conversations;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Workflows;
using Jarvis.Domain.Integrations;
using Jarvis.Infrastructure.Identity;
using Jarvis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class JarvisDbContext(DbContextOptions<JarvisDbContext> options)
    : IdentityDbContext<JarvisUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuthRefreshToken> AuthRefreshTokens => Set<AuthRefreshToken>();

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<AgentSessionState> AgentSessions => Set<AgentSessionState>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    public DbSet<ToolApproval> ToolApprovals => Set<ToolApproval>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<ConditionWatch> ConditionWatches => Set<ConditionWatch>();
    public DbSet<JarvisTask> Tasks => Set<JarvisTask>();
    public DbSet<StoredFileEntity> Files => Set<StoredFileEntity>();
    public DbSet<FileContentChunkEntity> FileContentChunks => Set<FileContentChunkEntity>();
    public DbSet<IntegrationCredential> IntegrationCredentials => Set<IntegrationCredential>();
    public DbSet<DailyBriefingPreference> DailyBriefings => Set<DailyBriefingPreference>();
    public DbSet<OwnerSettingEntity> OwnerSettings => Set<OwnerSettingEntity>();
    public DbSet<SkillEntity> Skills => Set<SkillEntity>();
    public DbSet<SkillRevisionEntity> SkillRevisions => Set<SkillRevisionEntity>();
    public DbSet<MessageFeedbackEntity> MessageFeedback => Set<MessageFeedbackEntity>();
    public DbSet<GraphEntityEntity> GraphEntities => Set<GraphEntityEntity>();
    public DbSet<GraphRelationEntity> GraphRelations => Set<GraphRelationEntity>();
    public DbSet<ChannelConnectionEntity> ChannelConnections => Set<ChannelConnectionEntity>();
    public DbSet<ChannelMessageEntity> ChannelMessages => Set<ChannelMessageEntity>();
    public DbSet<ChannelThreadEntity> ChannelThreads => Set<ChannelThreadEntity>();
    public DbSet<UiSurfaceEntity> UiSurfaces => Set<UiSurfaceEntity>();
    public DbSet<RemoteAgentEntity> RemoteAgents => Set<RemoteAgentEntity>();
    public DbSet<A2ATokenEntity> A2ATokens => Set<A2ATokenEntity>();
    public DbSet<BrowserSessionEntity> BrowserSessions => Set<BrowserSessionEntity>();
    public DbSet<BrowserStepEntity> BrowserSteps => Set<BrowserStepEntity>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectAuditMutation();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectAuditMutation();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RejectAuditMutation()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit events are append-only and cannot be modified or deleted.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<JarvisUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<AuthRefreshToken>(entity =>
        {
            entity.ToTable("auth_refresh_tokens");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Id).ValueGeneratedNever();
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(token => token.ReplacedByHash).HasMaxLength(128);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt });
            entity.HasOne<JarvisUser>().WithMany().HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
            entity.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.Entity<MemoryEntity>(entity =>
        {
            entity.ToTable("memories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Content).HasColumnName("content").IsRequired();
            entity.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
            entity.Property(x => x.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(200);
            entity.Property(x => x.GraphIndexedAt).HasColumnName("graph_indexed_at");
            entity.Property(x => x.Importance).HasColumnName("importance");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(60);
            entity.Property(x => x.SourceId).HasColumnName("source_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.ValidUntil).HasColumnName("valid_until");
            entity.Property(x => x.IsPinned).HasColumnName("is_pinned");
            entity.Property(x => x.SearchVector).HasColumnName("search_vector").HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('simple'::regconfig, content)", stored: true);
            entity.HasIndex(x => new { x.OwnerId, x.Kind });
            entity.HasIndex(x => x.SearchVector).HasMethod("gin");
            entity.HasIndex(x => x.Content).HasMethod("gin").HasOperators("gin_trgm_ops");
            entity.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<ToolApproval>(entity =>
        {
            entity.ToTable("tool_approvals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.RequestId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ToolCallId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ToolName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ArgumentsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ResumeStatus).HasMaxLength(20).HasDefaultValue("not_started").IsRequired();
            entity.Property(x => x.ResumeStartedAt);
            entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<JarvisTask>().WithMany().HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.OwnerId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.OwnerId, x.RequestId, x.ToolCallId })
                .IsUnique().HasDatabaseName("ux_tool_approvals_idempotency");
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.AgentRunId).HasColumnName("agent_run_id");
            entity.Property(x => x.Tool).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(160).IsRequired();
            entity.Property(x => x.RiskClass).HasColumnName("risk_class").HasMaxLength(40).IsRequired();
            entity.Property(x => x.ApprovalId).HasColumnName("approval_id");
            entity.Property(x => x.Timestamp).HasColumnName("timestamp");
            entity.Property(x => x.Success).HasColumnName("success");
            entity.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OwnerId, x.Timestamp });
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.ToTable("reminders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.DueAt).HasColumnName("due_at");
            entity.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.HasIndex(x => x.WorkflowId).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.DueAt });
            entity.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Type).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(2_000).IsRequired();
            entity.Property(x => x.SourceId).HasColumnName("source_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ReadAt).HasColumnName("read_at");
            entity.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        });

        modelBuilder.Entity<PushDevice>(entity =>
        {
            entity.ToTable("push_devices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Token).HasMaxLength(4096).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(16).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
        });

        modelBuilder.Entity<PushDelivery>(entity =>
        {
            entity.ToTable("push_deliveries");
            entity.HasKey(x => new { x.NotificationId, x.DeviceId });
            entity.Property(x => x.NotificationId).HasColumnName("notification_id");
            entity.Property(x => x.DeviceId).HasColumnName("device_id");
            entity.Property(x => x.Attempts).HasColumnName("attempts");
            entity.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            entity.Property(x => x.LeaseUntil).HasColumnName("lease_until");
            entity.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            entity.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PushDevice>().WithMany().HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.DeliveredAt, x.NextAttemptAt, x.LeaseUntil });
        });

        modelBuilder.Entity<ConditionWatch>(entity =>
        {
            entity.ToTable("condition_watches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.JsonPath).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Comparison).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Threshold).HasColumnName("threshold");
            entity.Property(x => x.IntervalMinutes).HasColumnName("interval_minutes");
            entity.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            entity.Property(x => x.LastCheckedAt).HasColumnName("last_checked_at");
            entity.Property(x => x.LastValue).HasColumnName("last_value");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.HasIndex(x => x.WorkflowId).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
        });

        modelBuilder.Entity<JarvisTask>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Prompt).HasColumnName("prompt").HasMaxLength(32_000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.UserMessageId).HasColumnName("user_message_id");
            entity.Property(x => x.ResultMessageId).HasColumnName("result_message_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.Property(x => x.Summary).HasMaxLength(8_000);
            entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.WorkflowId).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.CreatedAt });
            entity.HasIndex(x => new { x.OwnerId, x.ConversationId, x.Status });
            entity.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
        });

        modelBuilder.Entity<StoredFileEntity>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(500).IsRequired();
            entity.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(120).IsRequired();
            entity.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            entity.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            entity.Property(x => x.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.OwnerId, x.CreatedAt });
            entity.HasIndex(x => x.ObjectKey).IsUnique();
            entity.HasIndex(x => new { x.ProcessingStatus, x.ScheduleDispatchedAt });
        });

        modelBuilder.Entity<FileContentChunkEntity>(entity =>
        {
            entity.ToTable("file_content_chunks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.FileId).HasColumnName("file_id");
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(x => x.Content).HasColumnName("content").IsRequired();
            entity.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
            entity.Property(x => x.SearchText).HasColumnName("search_text").HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('simple'::regconfig, content)", stored: true);
            entity.HasOne<StoredFileEntity>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerId, x.FileId, x.ChunkIndex }).IsUnique();
            entity.HasIndex(x => x.SearchText).HasMethod("gin");
            entity.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<IntegrationCredential>(entity =>
        {
            entity.ToTable("integration_credentials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProtectedPayload).HasColumnName("protected_payload").IsRequired();
            entity.Property(x => x.SecretNamesJson).HasColumnName("secret_names").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => new { x.OwnerId, x.Provider }).IsUnique();
        });

        modelBuilder.Entity<DailyBriefingPreference>(entity =>
        {
            entity.ToTable("daily_briefings");
            entity.HasKey(x => x.OwnerId);
            entity.Property(x => x.OwnerId).HasColumnName("owner_id").ValueGeneratedNever();
            entity.Property(x => x.Enabled).HasColumnName("enabled");
            entity.Property(x => x.LocalTime).HasColumnName("local_time").HasColumnType("time without time zone");
            entity.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            entity.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            entity.Property(x => x.LastDeliveredDate).HasColumnName("last_delivered_date").HasColumnType("date");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.WorkflowId).IsUnique();
            entity.HasIndex(x => new { x.Enabled, x.ScheduleDispatchedAt });
        });

        modelBuilder.Entity<OwnerSettingEntity>(entity =>
        {
            entity.ToTable("owner_settings");
            entity.HasKey(x => new { x.OwnerId, x.Section });
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Section).HasColumnName("section").HasMaxLength(60);
            entity.Property(x => x.ValueJson).HasColumnName("value").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.Section);
        });

        modelBuilder.Entity<SkillEntity>(entity =>
        {
            entity.ToTable("skills");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(1_024).IsRequired();
            entity.Property(x => x.Instructions).HasColumnName("instructions").IsRequired();
            entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.IsLocked).HasColumnName("is_locked");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.UseCount).HasColumnName("use_count");
            entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.Status });
        });

        modelBuilder.Entity<SkillRevisionEntity>(entity =>
        {
            entity.ToTable("skill_revisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.SkillId).HasColumnName("skill_id");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(1_024).IsRequired();
            entity.Property(x => x.Instructions).HasColumnName("instructions").IsRequired();
            entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
            entity.Property(x => x.ChangeNote).HasColumnName("change_note").HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.HasOne<SkillEntity>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.SkillId, x.Version }).IsUnique();
        });

        modelBuilder.Entity<MessageFeedbackEntity>(entity =>
        {
            entity.ToTable("message_feedback");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.MessageId).HasColumnName("message_id");
            entity.Property(x => x.Rating).HasColumnName("rating").HasMaxLength(10).IsRequired();
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(1_000);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            entity.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerId, x.MessageId }).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.ProcessedAt });
        });

        modelBuilder.Entity<GraphEntityEntity>(entity =>
        {
            entity.ToTable("graph_entities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Key).HasColumnName("key").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
            entity.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500);
            entity.Property(x => x.AliasesJson).HasColumnName("aliases").HasMaxLength(2_000).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => new { x.OwnerId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
        });

        modelBuilder.Entity<GraphRelationEntity>(entity =>
        {
            entity.ToTable("graph_relations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.SubjectId).HasColumnName("subject_id");
            entity.Property(x => x.Predicate).HasColumnName("predicate").HasMaxLength(60).IsRequired();
            entity.Property(x => x.ObjectId).HasColumnName("object_id");
            entity.Property(x => x.ObjectValue).HasColumnName("object_value").HasMaxLength(300);
            entity.Property(x => x.ValidFrom).HasColumnName("valid_from");
            entity.Property(x => x.ValidTo).HasColumnName("valid_to");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.SourceMemoryId).HasColumnName("source_memory_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.HasOne<GraphEntityEntity>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GraphEntityEntity>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MemoryEntity>().WithMany().HasForeignKey(x => x.SourceMemoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerId, x.SubjectId, x.Predicate, x.ValidTo });
            entity.HasIndex(x => new { x.OwnerId, x.ObjectId });
        });

        modelBuilder.Entity<ChannelConnectionEntity>(entity =>
        {
            entity.ToTable("channel_connections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Account).HasColumnName("account").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Enabled).HasColumnName("enabled");
            entity.Property(x => x.AllowedSendersJson).HasColumnName("allowed_senders").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ForwardNotifications).HasColumnName("forward_notifications");
            entity.Property(x => x.NotifyRecipient).HasColumnName("notify_recipient").HasMaxLength(40);
            entity.Property(x => x.WebhookKey).HasColumnName("webhook_key").HasMaxLength(64).IsRequired();
            entity.Property(x => x.LastInboundAt).HasColumnName("last_inbound_at");
            entity.Property(x => x.LastOutboundAt).HasColumnName("last_outbound_at");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            entity.Property(x => x.NotificationsForwardedUntil).HasColumnName("notifications_forwarded_until");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.WebhookKey).IsUnique();
            entity.HasIndex(x => new { x.Kind, x.Account }).IsUnique();
            entity.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<ChannelMessageEntity>(entity =>
        {
            entity.ToTable("channel_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ConnectionId).HasColumnName("connection_id");
            entity.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(4).IsRequired();
            entity.Property(x => x.Peer).HasColumnName("peer").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Text).HasColumnName("text").IsRequired();
            entity.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(200);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.Error).HasColumnName("error").HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            entity.Property(x => x.LeaseUntil).HasColumnName("lease_until");
            entity.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ConnectionId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
            entity.HasIndex(x => new { x.Direction, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.ConnectionId, x.CreatedAt });
        });

        modelBuilder.Entity<ChannelThreadEntity>(entity =>
        {
            entity.ToTable("channel_threads");
            entity.HasKey(x => new { x.ConnectionId, x.Peer });
            entity.Property(x => x.ConnectionId).HasColumnName("connection_id");
            entity.Property(x => x.Peer).HasColumnName("peer").HasMaxLength(80);
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UiSurfaceEntity>(entity =>
        {
            entity.ToTable("ui_surfaces");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(80).IsRequired();
            entity.Property(x => x.SchemaJson).HasColumnName("schema").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.CompletedAction).HasColumnName("completed_action").HasMaxLength(40);
            entity.Property(x => x.ValuesJson).HasColumnName("values").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerId, x.ConversationId, x.CreatedAt });
        });

        modelBuilder.Entity<RemoteAgentEntity>(entity =>
        {
            entity.ToTable("remote_agents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
            entity.Property(x => x.Enabled).HasColumnName("enabled");
            entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<A2ATokenEntity>(entity =>
        {
            entity.ToTable("a2a_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<BrowserSessionEntity>(entity =>
        {
            entity.ToTable("browser_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.Goal).HasColumnName("goal").HasMaxLength(1_000).IsRequired();
            entity.Property(x => x.StartUrl).HasColumnName("start_url").HasMaxLength(500);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerId, x.ConversationId, x.Status });
        });

        modelBuilder.Entity<BrowserStepEntity>(entity =>
        {
            entity.ToTable("browser_steps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.SessionId).HasColumnName("session_id");
            entity.Property(x => x.Ordinal).HasColumnName("ordinal");
            entity.Property(x => x.Tool).HasColumnName("tool").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1_000).IsRequired();
            entity.Property(x => x.Success).HasColumnName("success");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(x => new { x.SessionId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Role).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Content).IsRequired();
            entity.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        });

        modelBuilder.Entity<AgentSessionState>(entity =>
        {
            entity.ToTable("agent_sessions");
            entity.HasKey(x => x.ConversationId);
            entity.Property(x => x.State).HasColumnType("jsonb").IsRequired();
            entity.HasOne<Conversation>().WithOne()
                .HasForeignKey<AgentSessionState>(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
