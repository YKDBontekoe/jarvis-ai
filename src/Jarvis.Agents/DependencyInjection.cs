using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Agents.ModelProviders;

namespace Jarvis.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddJarvisAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var executablePath = configuration["Codex:ExecutablePath"] ?? "codex";
        var model = configuration["Codex:Model"];
        var visionModel = configuration["Codex:VisionModel"];
        var enableWebSearch = configuration.GetValue("Codex:EnableWebSearch", true);
        if (string.IsNullOrWhiteSpace(model)) model = null;
        if (string.IsNullOrWhiteSpace(visionModel)) visionModel = null;
        var modelClasses = configuration.GetSection("Codex:ModelClasses").Get<Dictionary<string, string>>() ?? [];
        var turnTimeoutSeconds = configuration.GetValue("Codex:TurnTimeoutSeconds", 300);
        if (turnTimeoutSeconds is < 30 or > 1_800)
            throw new InvalidOperationException("Codex:TurnTimeoutSeconds must be between 30 and 1800.");
        services.AddSingleton<CodexProcessLimiter>();
        services.AddSingleton<IChatClient>(serviceProvider => new CodexCliChatClient(executablePath, model, visionModel,
                modelClasses, enableWebSearch, turnTimeoutSeconds,
                serviceProvider.GetRequiredService<CodexProcessLimiter>())
            .AsBuilder()
            .UseOpenTelemetry(
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                sourceName: "Jarvis.CodexChatClient",
                configure: telemetry => telemetry.EnableSensitiveData = false)
            .Build());
        services.AddSingleton<OpenAiCompatibleClientFactory>();
        services.AddScoped<IChatClientResolver, ChatClientResolver>();
        services.AddScoped<IConversationMemoryExtractor, ConversationMemoryExtractor>();
        services.AddScoped<MemoryReranker>();
        services.AddScoped<IAgentToolContributor, CoreAgentTools>();
        services.AddScoped<IAgentContextContributor, CoreAgentContext>();
        services.AddScoped<IAgentToolContributor, Skills.SkillToolContributor>();
        services.AddScoped<IAgentContextContributor, Skills.SkillContextContributor>();
        services.AddScoped<IAgentToolContributor, Persona.PersonaToolContributor>();
        services.AddScoped<IAgentContextContributor, Persona.PersonaContextContributor>();
        services.AddScoped<IMemoryEmbedder, Memory.ResolverMemoryEmbedder>();
        services.AddScoped<Memory.KnowledgeGraphExtractor>();
        services.AddScoped<Memory.MemoryIndexer>();
        services.AddScoped<IAgentToolContributor, Memory.KnowledgeGraphToolContributor>();
        services.AddScoped<IAgentContextContributor, Memory.KnowledgeGraphContextContributor>();
        services.AddSingleton<AgentTurnContext>();
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
        services.AddScoped<Learning.ReflectionService>();
        services.AddScoped<Learning.HeartbeatService>();
        services.AddScoped<JarvisAgentFactory>();
        services.AddScoped<IJarvisAgent, JarvisAgent>();
        return services;
    }
}
