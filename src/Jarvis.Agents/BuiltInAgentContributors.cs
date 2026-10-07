using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Integrations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Automations;
using Jarvis.Application.Workflows;
using Jarvis.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents;

internal sealed class CoreAgentTools(
    IJarvisTaskService taskService,
    IMemoryService memoryService,
    MemoryReranker reranker,
    IConditionWatchService watchService,
    IReminderService reminderService,
    IAutomationRuleService automationRules,
    IDailyBriefingRepository briefings,
    IFileSearchService fileSearch,
    IFileRepository fileRepository,
    IConversationFileScopeService conversationFileScope,
    IFileCitationCollector fileCitations,
    IDocumentCollectionRepository collections,
    IUserMcpServerRegistry mcpServers,
    IOwnerMcpPolicyStore mcpPolicy,
    McpToolHost mcpToolHost,
    IMcpOAuthService mcpOAuth,
    IAuditEventStore auditEvents,
    ICurrentUser currentUser,
    IMemoryRecallTracker recalls,
    CodexProcessLimiter codexProcessLimiter,
    IConfiguration configuration,
    ILoggerFactory loggerFactory,
    CodexExecutable codexExecutable,
    ICodingRunStore codingRuns,
    ICodingPullRequestService codingPullRequests,
    INotificationRepository notificationRepository,
    Jarvis.Application.Projects.IProjectStore projects,
    Jarvis.Application.Diagnostics.IRecentFaultLog? recentFaults = null,
    TimeProvider? timeProvider = null,
    Jarvis.Application.Devices.IDeviceTelemetryStore? deviceTelemetry = null,
    IMcpCatalog? mcpCatalog = null) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var taskTools = new TaskAgentTools(taskService, currentUser, context.Profile, projects, context.ConversationId);
        var memoryTools = new MemoryAgentTools(memoryService, reranker, auditEvents, currentUser,
            loggerFactory.CreateLogger<MemoryAgentTools>(), recalls, context.Profile);
        var watchTools = new ConditionWatchAgentTools(watchService, currentUser);
        var reminderTools = new ReminderAgentTools(reminderService, currentUser, briefings, deviceTelemetry);
        var automationTools = new AutomationAgentTools(automationRules, currentUser, briefings, timeProvider);
        var fileTools = new FileAgentTools(fileSearch, fileRepository, conversationFileScope, fileCitations, collections,
            currentUser, context.ConversationId, context.Profile);
        var clockTools = new ClockAgentTools(timeProvider ?? TimeProvider.System);
        var mcpServerTools = new McpServerAgentTools(mcpServers, mcpPolicy, configuration, currentUser, mcpToolHost,
            mcpOAuth, mcpCatalog);

        yield return AIFunctionFactory.Create(clockTools.GetCurrentTime);
        yield return AIFunctionFactory.Create(reminderTools.CreateReminderAsync);
        yield return AIFunctionFactory.Create(reminderTools.CreatePlaceReminderAsync);
        yield return AIFunctionFactory.Create(reminderTools.ListRemindersAsync);
        yield return AIFunctionFactory.Create(reminderTools.CancelReminderAsync);
        yield return AIFunctionFactory.Create(reminderTools.SnoozeReminderAsync);
        yield return AIFunctionFactory.Create(reminderTools.CompleteReminderAsync);
        yield return AIFunctionFactory.Create(fileTools.SearchFilesAsync);
        yield return AIFunctionFactory.Create(fileTools.ListFilesAsync);
        yield return AIFunctionFactory.Create(taskTools.ListTasksAsync);
        yield return AIFunctionFactory.Create(taskTools.CancelTaskAsync);
        yield return AIFunctionFactory.Create(watchTools.CreateConditionWatchAsync);
        yield return AIFunctionFactory.Create(watchTools.ListConditionWatchesAsync);
        yield return AIFunctionFactory.Create(watchTools.CancelConditionWatchAsync);
        yield return AIFunctionFactory.Create(automationTools.ListAutomationsAsync);
        yield return AIFunctionFactory.Create(automationTools.CreateAutomationAsync);
        yield return AIFunctionFactory.Create(automationTools.PreviewAutomationAsync);
        yield return AIFunctionFactory.Create(automationTools.ListAutomationTemplatesAsync);
        yield return AIFunctionFactory.Create(automationTools.CreateAutomationFromTemplateAsync);
        yield return AIFunctionFactory.Create(automationTools.EnableAutomationAsync);
        yield return AIFunctionFactory.Create(automationTools.DisableAutomationAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(automationTools.RunAutomationAsync));
        yield return AIFunctionFactory.Create(memoryTools.ListMemoriesAsync);
        yield return AIFunctionFactory.Create(memoryTools.SearchMemoryAsync);
        yield return AIFunctionFactory.Create(memoryTools.RememberAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(memoryTools.ForgetMemoryAsync));
        if (!context.IsBackgroundTask)
            yield return AIFunctionFactory.Create(taskTools.CreateTaskAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.ListMcpServersAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.ListHostMcpServersAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.ListMcpConnectionsAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.ListMcpServerToolsAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.DiscoverMcpServerToolsAsync));
        yield return AIFunctionFactory.Create(mcpServerTools.RequestMcpAuthorizationAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.SearchMcpCatalogAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.InstallMcpFromCatalogAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.AddMcpServerAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.AddMcpStdioServerAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.UpdateMcpServerAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.SetMcpServerEnabledAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.SetMcpServerToolsAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.InvokeMcpToolAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.ReadMcpResourceAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.GetMcpPromptAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.RemoveMcpServerAsync));

        if ((configuration.GetSection("Coding:Repositories").Get<CodingRepositoryOption[]>() ?? []).Length > 0)
        {
            var codingTools = new CodexCodingTools(configuration, loggerFactory.CreateLogger<CodexCodingTools>(),
                auditEvents, currentUser, codexProcessLimiter, codexExecutable, codingRuns);
            yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(codingTools.RunCodingTaskAsync));

            // Self-fix is opt-in: one allowlisted repository must be marked as Jarvis's own source.
            var selfRepository = configuration.GetSection("Coding:Repositories").GetChildren().FirstOrDefault(section =>
                section.GetValue<bool>("SelfFix") && !string.IsNullOrWhiteSpace(section["GitHubRepository"]) &&
                !string.IsNullOrWhiteSpace(section["Name"]));
            if (selfRepository is not null && !context.IsBackgroundTask)
            {
                var selfFix = new SelfFixAgentTools(codingTools, codingPullRequests, notificationRepository,
                    recentFaults, currentUser, selfRepository["Name"]!);
                yield return AIFunctionFactory.Create(selfFix.GetRecentJarvisFaults);
                yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(selfFix.ProposeJarvisFixAsync));
            }
        }
    }

    private sealed record CodingRepositoryOption(string Name, string Path);
}

internal sealed class CoreAgentContext(
    IDailyBriefingRepository briefings,
    IMemoryService memories,
    IJarvisTaskRepository tasks,
    IConditionWatchRepository watches,
    IReminderRepository reminders,
    IAutomationRuleRepository automations,
    IMemoryRecallTracker recalls,
    TimeProvider? timeProvider = null,
    Jarvis.Application.Learning.ITurnTraceCollector? trace = null) : IAgentContextContributor
{
    public int Order => 0;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
    [
        new ClockContextProvider(briefings, context.OwnerId, timeProvider ?? TimeProvider.System),
        new PersonalMemoryContextProvider(memories, context.OwnerId, recalls, context.Profile, trace),
        new ActiveTasksContextProvider(tasks, context.OwnerId, context.ExecutingTaskId),
        new ActiveConditionWatchesContextProvider(watches, context.OwnerId),
        new LinkedConversationContextProvider(reminders, automations, context.OwnerId, context.ConversationId)
    ];
}
