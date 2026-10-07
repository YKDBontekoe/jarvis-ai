using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Devices;
using Jarvis.Domain.Files;
using Jarvis.Domain.Automations;
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
    public DbSet<ConversationFileAttachmentEntity> ConversationFileAttachments => Set<ConversationFileAttachmentEntity>();
    public DbSet<ConversationCollectionAttachmentEntity> ConversationCollectionAttachments =>
        Set<ConversationCollectionAttachmentEntity>();
    public DbSet<IntegrationCredential> IntegrationCredentials => Set<IntegrationCredential>();
    public DbSet<DailyBriefingPreference> DailyBriefings => Set<DailyBriefingPreference>();
    public DbSet<WeeklyReview> WeeklyReviews => Set<WeeklyReview>();
    public DbSet<Jarvis.Domain.Projects.Project> Projects => Set<Jarvis.Domain.Projects.Project>();
    public DbSet<OwnerSettingEntity> OwnerSettings => Set<OwnerSettingEntity>();
    public DbSet<JournalEntryEntity> JournalEntries => Set<JournalEntryEntity>();
    public DbSet<ExpenseEntity> Expenses => Set<ExpenseEntity>();
    public DbSet<InboxThreadEntity> InboxThreads => Set<InboxThreadEntity>();
    public DbSet<CommitmentEntity> Commitments => Set<CommitmentEntity>();
    public DbSet<RoutineSuggestionEntity> RoutineSuggestions => Set<RoutineSuggestionEntity>();
    public DbSet<LearningSignalEntity> LearningSignals => Set<LearningSignalEntity>();
    public DbSet<TurnTraceEntity> TurnTraces => Set<TurnTraceEntity>();
    public DbSet<DecisionEntity> Decisions => Set<DecisionEntity>();
    public DbSet<BudgetEntity> Budgets => Set<BudgetEntity>();
    public DbSet<FinancialAccountEntity> FinancialAccounts => Set<FinancialAccountEntity>();
    public DbSet<HoldingEntity> Holdings => Set<HoldingEntity>();
    public DbSet<InvestmentTradeEntity> InvestmentTrades => Set<InvestmentTradeEntity>();
    public DbSet<PricePointEntity> PricePoints => Set<PricePointEntity>();
    public DbSet<LibraryItemEntity> LibraryItems => Set<LibraryItemEntity>();
    public DbSet<FlashcardEntity> Flashcards => Set<FlashcardEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();
    public DbSet<MissionStepEntity> MissionSteps => Set<MissionStepEntity>();
    public DbSet<MissionNoteEntity> MissionNotes => Set<MissionNoteEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<HabitEntity> Habits => Set<HabitEntity>();
    public DbSet<HabitCheckInEntity> HabitCheckIns => Set<HabitCheckInEntity>();
    public DbSet<PersonEntity> People => Set<PersonEntity>();
    public DbSet<PersonChannelLinkEntity> PersonChannelLinks => Set<PersonChannelLinkEntity>();
    public DbSet<SkillEntity> Skills => Set<SkillEntity>();
    public DbSet<SkillRevisionEntity> SkillRevisions => Set<SkillRevisionEntity>();
    public DbSet<MessageFeedbackEntity> MessageFeedback => Set<MessageFeedbackEntity>();
    public DbSet<GraphEntityEntity> GraphEntities => Set<GraphEntityEntity>();
    public DbSet<GraphRelationEntity> GraphRelations => Set<GraphRelationEntity>();
    public DbSet<ChannelConnectionEntity> ChannelConnections => Set<ChannelConnectionEntity>();
    public DbSet<ChannelMessageEntity> ChannelMessages => Set<ChannelMessageEntity>();
    public DbSet<ChannelThreadEntity> ChannelThreads => Set<ChannelThreadEntity>();
    public DbSet<WhatsAppChatEntity> WhatsAppChats => Set<WhatsAppChatEntity>();
    public DbSet<WhatsAppMessageEntity> WhatsAppMessages => Set<WhatsAppMessageEntity>();
    public DbSet<WhatsAppMessageMediaEntity> WhatsAppMessageMedia => Set<WhatsAppMessageMediaEntity>();
    public DbSet<UiSurfaceEntity> UiSurfaces => Set<UiSurfaceEntity>();
    public DbSet<RemoteAgentEntity> RemoteAgents => Set<RemoteAgentEntity>();
    public DbSet<A2ATokenEntity> A2ATokens => Set<A2ATokenEntity>();
    public DbSet<BrowserSessionEntity> BrowserSessions => Set<BrowserSessionEntity>();
    public DbSet<BrowserStepEntity> BrowserSteps => Set<BrowserStepEntity>();
    public DbSet<ModelUsageEventEntity> ModelUsageEvents => Set<ModelUsageEventEntity>();
    public DbSet<DeviceTelemetry> DeviceTelemetry => Set<DeviceTelemetry>();
    public DbSet<McpOAuthSession> McpOAuthSessions => Set<McpOAuthSession>();
    public DbSet<CodingRun> CodingRuns => Set<CodingRun>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
    public DbSet<AutomationWebhookEntity> AutomationWebhooks => Set<AutomationWebhookEntity>();

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
