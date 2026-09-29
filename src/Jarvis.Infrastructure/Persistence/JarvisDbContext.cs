using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Devices;
using Jarvis.Domain.Files;
using Jarvis.Domain.Integrations;
using Jarvis.Domain.Profiles;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Identity;
using Jarvis.Infrastructure.Persistence.Configurations.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class JarvisDbContext(DbContextOptions<JarvisDbContext> options)
    : IdentityDbContext<JarvisUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuthRefreshToken> AuthRefreshTokens => Set<AuthRefreshToken>();

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<AgentSessionState> AgentSessions => Set<AgentSessionState>();
    public DbSet<AssistantProfile> AssistantProfiles => Set<AssistantProfile>();
    public DbSet<DocumentCollection> DocumentCollections => Set<DocumentCollection>();
    public DbSet<DocumentCollectionFileEntity> DocumentCollectionFiles => Set<DocumentCollectionFileEntity>();
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
    public DbSet<ModelUsageEventEntity> ModelUsageEvents => Set<ModelUsageEventEntity>();
    public DbSet<DeviceTelemetry> DeviceTelemetry => Set<DeviceTelemetry>();
    public DbSet<McpOAuthSession> McpOAuthSessions => Set<McpOAuthSession>();
    public DbSet<CodingRun> CodingRuns => Set<CodingRun>();

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
        JarvisPersistenceInfrastructureConfiguration.Configure(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JarvisDbContext).Assembly);
    }
}
