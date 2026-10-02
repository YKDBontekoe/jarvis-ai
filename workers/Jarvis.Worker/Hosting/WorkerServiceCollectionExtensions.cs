using Jarvis.Agents;
using Jarvis.Application.Approvals;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Integrations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Infrastructure;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Mcp;
using Jarvis.Memory;
using Jarvis.Worker.Activities;
using Jarvis.Worker.Files;
using Jarvis.Worker.Learning;
using Jarvis.Workflows;
using Microsoft.Extensions.Options;

namespace Jarvis.Worker.Hosting;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddJarvisWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileProcessingOptions>()
            .Bind(configuration.GetSection(FileProcessingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FileProcessingOptions>, FileProcessingOptionsValidator>();

        services.AddJarvisInfrastructure(configuration);
        services.AddScoped<WorkerCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<WorkerCurrentUser>());
        services.AddScoped<IJarvisTaskRepository, WorkflowRepository>();
        services.AddScoped<IJarvisTaskService, JarvisTaskService>();
        services.AddSingleton<ITaskRunAbort, TaskRunAbort>();
        services.AddSingleton<TemporalReminderScheduler>();
        services.AddSingleton<IFileProcessingScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<IConditionWatchScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<IDailyBriefingScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<IHeartbeatScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<IDreamingScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<Jarvis.Application.Reviews.IWeeklyReviewScheduler>(sp =>
            sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<Jarvis.Application.Habits.IHabitCheckInScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddHostedService<TemporalWorkflowReconciler>();
        services.AddHostedService<MemoryIndexingWorker>();
        services.AddHostedService<TemporalWorkerHostedService>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<IConditionWatchService, ConditionWatchService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDailyBriefingService, DailyBriefingService>();
        services.AddScoped<Jarvis.Application.Reviews.IWeeklyReviewService, WeeklyReviewService>();
        services.AddScoped<IAutomationRuleService, AutomationRuleService>();
        services.AddScoped<IAutomationTriggerPublisher, AutomationTriggerPublisher>();
        services.AddScoped<IAutomationRunExecutor, AutomationRunExecutor>();
        services.AddScoped<AutomationConditionEvaluator>();
        services.AddScoped<IAutomationMetrics, AutomationMetrics>();
        services.AddSingleton<IAutomationScheduler>(sp => sp.GetRequiredService<TemporalReminderScheduler>());
        services.AddSingleton<PublicJsonMetricReader>();
        services.AddScoped<ICalendarFeed, CalendarFeed>();
        services.AddScoped<WatchMetricReader>();
        services.AddJarvisMemory();
        services.AddScoped<McpToolHost>();
        services.AddJarvisAgent(configuration);
        services.AddHttpClient("a2a", client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddSingleton<StoredFileProcessor>();
        services.AddSingleton<ReminderActivities>();
        services.AddSingleton<FileProcessingActivities>();
        services.AddSingleton<JarvisTaskActivities>();
        services.AddSingleton<ConditionWatchActivities>();
        services.AddSingleton<DailyBriefingActivities>();
        services.AddSingleton<AssistantHeartbeatActivities>();
        services.AddSingleton<AssistantDreamingActivities>();
        services.AddSingleton<AutomationRunActivities>();
        services.AddSingleton<AutomationScheduleActivities>();
        services.AddSingleton<AutomationPollActivities>();
        services.AddSingleton<WeeklyReviewActivities>();
        services.AddSingleton<HabitCheckInActivities>();

        return services;
    }
}
