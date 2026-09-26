using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;

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
        services.AddSingleton<IChatClient>(serviceProvider => new CodexCliChatClient(executablePath, model, visionModel,
                modelClasses, enableWebSearch)
            .AsBuilder()
            .UseOpenTelemetry(
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                sourceName: "Jarvis.CodexChatClient",
                configure: telemetry => telemetry.EnableSensitiveData = false)
            .Build());
        services.AddScoped<IConversationMemoryExtractor, ConversationMemoryExtractor>();
        services.AddScoped<MemoryReranker>();
        services.AddScoped<JarvisAgentFactory>();
        services.AddScoped<IJarvisAgent, JarvisAgent>();
        return services;
    }
}
