using System.Net.Http.Headers;
using Jarvis.Agents;
using Jarvis.Api.Conversations;
using Jarvis.Api.Errors;
using Jarvis.Api.Notifications;
using Jarvis.Api.Realtime;
using Jarvis.Api.Security;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Integrations;
using Jarvis.Application.Automations;
using Jarvis.Application.Workflows;
using Jarvis.Infrastructure;
using Jarvis.Infrastructure.Identity;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Mcp;
using Jarvis.Memory;
using Jarvis.Workflows;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Jarvis.Api.Hosting;

internal static class ApiServiceRegistration
{
    public static IServiceCollection AddJarvisChannels(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(Channels.ChannelOptions.From(configuration));
        services.AddHttpClient<Channels.WhatsAppCloudTransport>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<Channels.SignalRestTransport>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<Channels.WhatsAppBridgeClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient("signal", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<Channels.IChannelTransport>(provider => provider.GetRequiredService<Channels.WhatsAppCloudTransport>());
        services.AddScoped<Channels.IChannelTransport>(provider => provider.GetRequiredService<Channels.SignalRestTransport>());
        services.AddScoped<Channels.WhatsAppLinkedTransport>();
        services.AddScoped<Channels.IChannelTransport>(provider => provider.GetRequiredService<Channels.WhatsAppLinkedTransport>());
        services.AddSingleton<Channels.ChannelLinkService>();
        services.AddScoped<Channels.ChannelMessenger>();
        services.AddScoped<Channels.ChannelMessageRouter>();
        services.AddHostedService<Channels.ChannelInboundProcessor>();
        services.AddHostedService<Channels.SignalReceiver>();
        services.AddHostedService<Channels.WhatsAppLinkedReceiver>();
        services.AddHostedService<Channels.ChannelNotificationForwarder>();
        return services;
    }

    public static void ValidateProductionConfiguration(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment()) return;
        if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsAtStartup"))
            throw new InvalidOperationException("Database:ApplyMigrationsAtStartup is supported only in Development. Run the 'migrate' command before deploying production services.");
        if (string.IsNullOrWhiteSpace(builder.Configuration["Antivirus:Host"]))
            throw new InvalidOperationException("Configure Antivirus:Host with a private ClamAV daemon before running Jarvis outside Development.");
        if (string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeysDirectory"]))
            throw new InvalidOperationException("Configure DataProtection:KeysDirectory as a persistent, private writable directory before running Jarvis outside Development.");
        if (new[] { "LiveKit:ApiKey", "LiveKit:ApiSecret", "LiveKit:InternalUrl", "LiveKit:PublicUrl", "Voice:WorkerSecret" }
            .Any(key => string.IsNullOrWhiteSpace(builder.Configuration[key])))
            throw new InvalidOperationException("Configure LiveKit and Voice:WorkerSecret before running Jarvis outside Development.");
    }

    public static IServiceCollection AddJarvisAuthentication(this IServiceCollection services,
        AccountTokenOptions accountTokens, bool isDevelopment)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = !isDevelopment;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = accountTokens.ValidationParameters();
                options.Events = ApiErrorServiceCollectionExtensions.CreateJarvisJwtBearerEvents(new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out _))
                            context.Fail("The access token must identify an account.");
                        return Task.CompletedTask;
                    },
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && context.Request.Path.StartsWithSegments("/hubs/events"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                });
            });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<OwnerExecutionContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }

    public static IServiceCollection AddJarvisApi(this IServiceCollection services, IConfiguration configuration,
        AccountTokenOptions accountTokens)
    {
        services.AddJarvisApiErrors();
        services.AddJarvisInfrastructure(configuration);
        services.AddJarvisIdentity(accountTokens);
        services.AddJarvisMemory();
        services.AddSingleton<Jarvis.Application.Diagnostics.RecentFaultLog>();
        services.AddSingleton<Jarvis.Application.Diagnostics.IRecentFaultLog>(provider =>
            provider.GetRequiredService<Jarvis.Application.Diagnostics.RecentFaultLog>());
        services.AddSingleton<ILoggerProvider>(provider =>
            new RecentFaultLoggerProvider(provider.GetRequiredService<Jarvis.Application.Diagnostics.IRecentFaultLog>()));
        services.AddSingleton<TemporalReminderScheduler>();
        services.AddSingleton<IFileProcessingScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<IConditionWatchScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<IDailyBriefingScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<Jarvis.Application.Learning.IHeartbeatScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<Jarvis.Application.Learning.IDreamingScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<Jarvis.Application.People.IPeopleCheckInScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<IConditionWatchService, ConditionWatchService>();
        services.AddScoped<IDailyBriefingService, DailyBriefingService>();
        services.AddScoped<IAutomationRuleService, AutomationRuleService>();
        services.AddScoped<IAutomationApprovalResolver, AutomationApprovalResolver>();
        services.AddScoped<IAutomationTriggerPublisher, AutomationTriggerPublisher>();
        services.AddSingleton<IAutomationScheduler>(provider => provider.GetRequiredService<TemporalReminderScheduler>());
        services.AddScoped<IJarvisTaskRepository, WorkflowRepository>();
        services.AddScoped<IJarvisTaskService, JarvisTaskService>();
        services.AddSingleton<PublicJsonMetricReader>();
        services.AddScoped<ICalendarFeed, CalendarFeed>();
        services.AddScoped<WatchMetricReader>();
        services.AddScoped<Jarvis.Application.Integrations.IMcpOAuthService, Jarvis.Mcp.McpOAuthService>();
        services.AddSingleton<ITaskRunAbort, TaskRunAbort>();
        services.AddJarvisAgent(configuration);
        services.AddSingleton<Jarvis.Application.Realtime.IRealtimePublisher, Devices.SignalRRealtimePublisher>();
        services.AddSingleton<Jarvis.Application.Devices.IDeviceInvoker, Devices.SignalRDeviceInvoker>();
        services.AddHttpClient("a2a", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<McpToolHost>();
        services.AddScoped<AgentRunCoordinator>();
        services.AddScoped<ConversationTurnService>();
        services.AddScoped<ApprovalDecisionService>();
        services.AddSingleton<RemoteQueryHost>();
        services.AddSingleton<RemoteQueryExecutor>();
        services.AddSingleton<VoiceBackendSession>();
        services.AddSingleton<VoiceRuntime>();
        services.AddHostedService(provider => provider.GetRequiredService<VoiceRuntime>());
        services.AddHttpClient("npm-registry", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("jarvis", "1.0"));
        });
        services.AddSingleton<CodexInstallation>();
        services.AddHttpClient("firebase-messaging", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHostedService<NotificationPushWorker>();
        services.AddHostedService<NotificationRealtimeWorker>();
        services.AddJarvisChannels(configuration);
        services.AddSignalR();
        services.AddOpenApi();
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                         ["http://localhost:5137", "http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
        return services;
    }
}
