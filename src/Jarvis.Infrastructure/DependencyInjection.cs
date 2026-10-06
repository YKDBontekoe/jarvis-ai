using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Audit;
using Jarvis.Application.Approvals;
using Jarvis.Application.Workflows;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Files;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Infrastructure.Search;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddJarvisInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("jarvis")
            ?? configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException("PostgreSQL connection string 'jarvis' is required.");

        var protection = services.AddDataProtection().SetApplicationName("Jarvis");
        var keysDirectory = configuration["DataProtection:KeysDirectory"];
        if (!string.IsNullOrWhiteSpace(keysDirectory))
        {
            Directory.CreateDirectory(keysDirectory);
            protection.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
        }

        services.AddDbContext<JarvisDbContext>(options => options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
        services.AddScoped<ConversationStore>();
        services.AddScoped<IConversationStore>(provider => provider.GetRequiredService<ConversationStore>());
        services.AddScoped<IConversationHistory>(provider => provider.GetRequiredService<ConversationStore>());
        services.AddScoped<IConversationRunLock>(serviceProvider => new PostgresConversationRunLock(
            connectionString,
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgresConversationRunLock>>()));
        services.AddScoped<IMemoryRepository, MemoryRepository>();
        services.AddScoped<IMemoryIndexRepository, MemoryIndexRepository>();
        services.AddScoped<IKnowledgeGraphRepository, KnowledgeGraphRepository>();
        services.AddScoped<Jarvis.Application.Journal.IJournalRepository, JournalRepository>();
        services.AddScoped<Jarvis.Application.Expenses.IExpenseRepository, ExpenseRepository>();
        services.AddScoped<Jarvis.Application.WhatsApp.IWhatsAppAssistantRepository, WhatsAppAssistantRepository>();
        services.AddScoped<Jarvis.Application.Expenses.IExpenseService, Jarvis.Application.Expenses.ExpenseService>();
        services.AddScoped<Jarvis.Application.Habits.IHabitRepository, HabitRepository>();
        services.AddScoped<Jarvis.Application.Habits.IHabitService, Jarvis.Application.Habits.HabitService>();
        services.AddScoped<Jarvis.Application.People.IPeopleRepository, PeopleRepository>();
        services.AddScoped<Jarvis.Application.People.IPeopleService, Jarvis.Application.People.PeopleService>();
        services.AddScoped<Jarvis.Application.People.IPeopleCheckInService, Jarvis.Application.People.PeopleCheckInService>();
        services.AddScoped<Jarvis.Application.People.Radar.IPersonLinkRepository, PersonLinkRepository>();
        services.AddScoped<Jarvis.Application.People.Radar.IChatActivityStats, WhatsAppAssistantRepository>();
        services.AddScoped<Jarvis.Application.People.Radar.IRelationshipRadarService,
            Jarvis.Application.People.Radar.RelationshipRadarService>();
        services.AddScoped<Jarvis.Application.Channels.IChannelRepository, ChannelRepository>();
        services.AddScoped<Jarvis.Application.Devices.IDeviceTelemetryStore, DeviceTelemetryStore>();
        services.AddScoped<Jarvis.Application.Integrations.IMcpOAuthSessionStore, McpOAuthSessionStore>();
        services.AddScoped<ICodingRunStore, CodingRunStore>();
        services.AddScoped<ICodingPullRequestService, Jarvis.Infrastructure.Coding.GitHubCodingPullRequestService>();
        services.AddScoped<Jarvis.Application.Home.IHomeBriefingService, Jarvis.Infrastructure.Home.HomeBriefingService>();
        services.AddScoped<Jarvis.Application.Surfaces.IUiSurfaceRepository, UiSurfaceRepository>();
        services.AddScoped<Jarvis.Application.Agents.IRemoteAgentRepository, RemoteAgentRepository>();
        services.AddScoped<Jarvis.Application.Agents.IA2ATokenRepository, A2ATokenRepository>();
        services.AddScoped<Jarvis.Application.Browser.IBrowserSessionStore, BrowserSessionRepository>();
        services.AddScoped<IToolApprovalStore, ToolApprovalStore>();
        services.AddScoped<IStandingApprovalService, StandingApprovalService>();
        services.AddScoped<IApprovalPolicy, ApprovalPolicy>();
        services.AddScoped<Jarvis.Application.Memory.IConversationMemoryGate, Jarvis.Application.Memory.ConversationMemoryGate>();
        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<IReminderRepository, WorkflowRepository>();
        services.AddScoped<INotificationRepository, WorkflowRepository>();
        services.AddScoped<IPushDeviceRepository, WorkflowRepository>();
        services.AddScoped<IPushDeliveryQueue, PushDeliveryRepository>();
        services.AddScoped<INotificationFeed, NotificationFeed>();
        services.AddScoped<IConditionWatchRepository, ConditionWatchRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFileContentRepository, FileContentRepository>();
        services.AddScoped<IIntegrationCredentialStore, IntegrationCredentialStore>();
        services.AddScoped<IUserMcpServerRegistry, UserMcpServerRegistry>();
        services.AddHttpClient<IMcpCatalog, Jarvis.Infrastructure.Integrations.OfficialMcpRegistryCatalog>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("jarvis-mcp-catalog/1.0");
        });
        services.AddScoped<IOwnerMcpPolicyStore, OwnerMcpPolicyStore>();
        services.AddScoped<IDailyBriefingRepository, DailyBriefingRepository>();
        services.AddScoped<IAutomationRuleRepository, AutomationRuleRepository>();
        services.AddScoped<IAutomationRunRepository, AutomationRunRepository>();
        services.AddScoped<IAutomationChannelSender, Jarvis.Infrastructure.Automations.AutomationChannelSender>();
        services.AddScoped<IDailyBriefingNarrator, NoOpDailyBriefingNarrator>();
        services.AddScoped<Jarvis.Application.Reviews.IWeeklyReviewRepository, WeeklyReviewRepository>();
        services.AddScoped<Jarvis.Application.Reviews.IWeeklyReviewNarrator, Jarvis.Application.Reviews.NoOpWeeklyReviewNarrator>();
        services.AddScoped<Jarvis.Application.Settings.IOwnerSettingsStore, OwnerSettingsStore>();
        services.AddScoped<Jarvis.Application.Settings.IOwnerDirectory, OwnerDirectory>();
        services.AddScoped<Jarvis.Application.Skills.ISkillRepository, SkillRepository>();
        services.AddScoped<Jarvis.Application.Profiles.IAssistantProfileRepository, AssistantProfileRepository>();
        services.AddScoped<Jarvis.Application.Profiles.IAssistantProfileService, Jarvis.Application.Profiles.AssistantProfileService>();
        services.AddScoped<Jarvis.Application.Files.IDocumentCollectionRepository, DocumentCollectionRepository>();
        services.AddScoped<Jarvis.Application.Projects.IProjectStore, ProjectStore>();
        services.AddScoped<Jarvis.Application.Persona.IMessageFeedbackRepository, MessageFeedbackRepository>();
        services.AddScoped<Jarvis.Application.Usage.IModelUsageRecorder, ModelUsageRecorder>();
        services.AddScoped<Jarvis.Application.Usage.IUsageDashboard, UsageDashboardService>();
        services.AddScoped<Jarvis.Application.Persona.PersonaService>();
        services.AddScoped<IFileService, FileService>();
        services.AddHttpClient();
        services.AddScoped<IFileMalwareScanner, ClamAvVirusScanner>();
        services.AddScoped<IFileSearchService, FileSearchService>();
        services.AddScoped<IConversationFileContextRepository, ConversationFileContextRepository>();
        services.AddScoped<IConversationFileScopeService, ConversationFileScopeService>();
        services.AddScoped<IFileCitationCollector, FileCitationCollector>();
        services.AddScoped<IFileCitationResolver, FileCitationResolver>();
        services.AddJarvisFederatedSearch(configuration);
        services.AddJarvisLifeFeatures();
        services.AddScoped<IObjectStorage, S3ObjectStorage>();
        var objectStorage = configuration.GetSection("ObjectStorage");
        var serviceUrl = objectStorage["ServiceUrl"] ?? throw new InvalidOperationException("ObjectStorage:ServiceUrl is required.");
        var accessKey = objectStorage["AccessKey"] ?? throw new InvalidOperationException("ObjectStorage:AccessKey is required.");
        var secretKey = objectStorage["SecretKey"] ?? throw new InvalidOperationException("ObjectStorage:SecretKey is required.");
        var region = objectStorage["Region"] ?? "us-east-1";
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = region,
            ForcePathStyle = true,
            // Garage/SeaweedFS accept signed payloads but not all AWS checksum trailer formats.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
        }));
        return services;
    }
}
