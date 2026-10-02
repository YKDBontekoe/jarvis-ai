using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Agents.ModelProviders;
using Jarvis.Agents.Telemetry;

namespace Jarvis.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddJarvisAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var model = configuration["Codex:Model"];
        var visionModel = configuration["Codex:VisionModel"];
        var enableWebSearch = configuration.GetValue("Codex:EnableWebSearch", true);
        if (string.IsNullOrWhiteSpace(model)) model = null;
        if (string.IsNullOrWhiteSpace(visionModel)) visionModel = null;
        var modelClasses = configuration.GetSection("Codex:ModelClasses").Get<Dictionary<string, string>>() ?? [];
        var turnTimeoutSeconds = configuration.GetValue("Codex:TurnTimeoutSeconds", 300);
        if (turnTimeoutSeconds is < 30 or > 1_800)
            throw new InvalidOperationException("Codex:TurnTimeoutSeconds must be between 30 and 1800.");
        services.AddSingleton(CodexExecutable.From(configuration));
        services.AddSingleton<CodexProcessLimiter>();
        var recordAiContent = configuration.GetValue("Sentry:RecordAiContent", false);
        services.AddSingleton<IChatClient>(serviceProvider => SentryChatInstrumentation.Instrument(
                new CodexCliChatClient(
                    serviceProvider.GetRequiredService<CodexExecutable>(), model, visionModel,
                    modelClasses, enableWebSearch, turnTimeoutSeconds,
                    serviceProvider.GetRequiredService<CodexProcessLimiter>()),
                recordAiContent)
            .AsBuilder()
            .UseOpenTelemetry(
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                sourceName: "Jarvis.CodexChatClient",
                configure: telemetry => telemetry.EnableSensitiveData = false)
            .Build());
        services.AddSingleton<OpenAiCompatibleClientFactory>();
        services.AddHttpClient<OpenRouterCatalog>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddTransient<Jarvis.Application.Usage.IModelPriceLookup>(provider =>
            provider.GetRequiredService<OpenRouterCatalog>());
        services.AddScoped<IChatClientResolver, ChatClientResolver>();
        services.AddScoped<IConversationMemoryExtractor, ConversationMemoryExtractor>();
        services.AddScoped<MemoryReranker>();
        services.AddScoped<IAgentToolContributor, CoreAgentTools>();
        services.AddScoped<IAgentContextContributor, CoreAgentContext>();
        services.AddScoped<IAgentToolContributor, McpSetupToolContributor>();
        services.AddScoped<IAgentContextContributor, McpContextContributor>();
        services.AddScoped<Jarvis.Application.Integrations.IMcpOAuthService, Jarvis.Mcp.McpOAuthService>();
        services.AddScoped<IAgentContextContributor, Profiles.ProfileContextContributor>();
        services.AddScoped<IAgentContextContributor, Projects.ProjectContextContributor>();
        services.AddScoped<IAgentToolContributor, Skills.SkillToolContributor>();
        services.AddScoped<IAgentContextContributor, Skills.SkillContextContributor>();
        services.AddScoped<IAgentToolContributor, Journal.JournalToolContributor>();
        services.AddScoped<IAgentContextContributor, Journal.JournalContextContributor>();
        services.AddScoped<IAgentToolContributor, Planner.PlannerToolContributor>();
        services.AddScoped<IAgentContextContributor, Planner.PlannerContextContributor>();
        services.AddScoped<IAgentToolContributor, Expenses.ExpenseToolContributor>();
        services.AddScoped<IAgentContextContributor, Expenses.ExpenseContextContributor>();
        services.AddScoped<Jarvis.Application.Expenses.IReceiptReader, Expenses.ReceiptReader>();
        services.AddScoped<IAgentToolContributor, Habits.HabitToolContributor>();
        services.AddScoped<IAgentContextContributor, Habits.HabitContextContributor>();
        services.AddScoped<IAgentToolContributor, Persona.PersonaToolContributor>();
        services.AddScoped<IAgentContextContributor, Persona.PersonaContextContributor>();
        services.AddScoped<IAgentContextContributor, Learning.UserSummaryContextContributor>();
        services.AddScoped<IMemoryEmbedder, Memory.ResolverMemoryEmbedder>();
        services.AddScoped<Memory.KnowledgeGraphExtractor>();
        services.AddScoped<Memory.SearchHintGenerator>();
        services.AddScoped<Memory.MemoryIndexer>();
        services.AddScoped<IAgentToolContributor, Memory.KnowledgeGraphToolContributor>();
        services.AddScoped<IAgentContextContributor, Memory.KnowledgeGraphContextContributor>();
        services.AddSingleton<Jarvis.Application.Realtime.IRealtimePublisher, Jarvis.Application.Realtime.NoOpRealtimePublisher>();
        services.AddSingleton<Jarvis.Application.Devices.IDeviceInvoker, Jarvis.Application.Devices.NoOpDeviceInvoker>();
        services.AddScoped<IAgentToolContributor, Surfaces.SurfaceToolContributor>();
        services.AddScoped<IAgentContextContributor, Surfaces.SurfaceContextContributor>();
        services.AddScoped<IAgentToolContributor, Networking.RemoteAgentToolContributor>();
        services.AddScoped<IAgentContextContributor, Networking.RemoteAgentContextContributor>();
        services.AddScoped<IAgentToolContributor, Devices.DeviceToolContributor>();
        services.AddScoped<IAgentContextContributor, Devices.DeviceContextContributor>();
        services.AddScoped<IAgentToolContributor, Browser.BrowserToolContributor>();
        services.AddScoped<IAgentContextContributor, Browser.BrowserContextContributor>();
        services.AddScoped<IAgentContextContributor, FileContextContributor>();
        services.AddSingleton<Jarvis.Application.Learning.IMemoryRecallTracker, Learning.MemoryRecallTracker>();
        services.AddScoped<Learning.ReflectionService>();
        services.AddScoped<Learning.HeartbeatService>();
        services.AddScoped<Learning.DreamingService>();
        services.AddScoped<IDailyBriefingNarrator, DailyBriefingNarrator>();
        services.AddScoped<Jarvis.Application.Reviews.IWeeklyReviewNarrator, WeeklyReviewNarrator>();
        services.AddScoped<JarvisAgentFactory>();
        services.AddScoped<VoiceSessionContext>();
        services.AddScoped<IJarvisAgent, JarvisAgent>();
        return services;
    }
}
