using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Integrations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
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
    IDailyBriefingRepository briefings,
    IFileSearchService fileSearch,
    IFileRepository fileRepository,
    IUserMcpServerRegistry mcpServers,
    IOwnerMcpPolicyStore mcpPolicy,
    McpToolHost mcpToolHost,
    IAuditEventStore auditEvents,
    ICurrentUser currentUser,
    IMemoryRecallTracker recalls,
    CodexProcessLimiter codexProcessLimiter,
    IConfiguration configuration,
    ILoggerFactory loggerFactory,
    CodexExecutable codexExecutable,
    TimeProvider? timeProvider = null) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var taskTools = new TaskAgentTools(taskService, currentUser);
        var memoryTools = new MemoryAgentTools(memoryService, reranker, auditEvents, currentUser,
            loggerFactory.CreateLogger<MemoryAgentTools>(), recalls);
        var watchTools = new ConditionWatchAgentTools(watchService, currentUser);
        var reminderTools = new ReminderAgentTools(reminderService, currentUser, briefings);
        var fileTools = new FileAgentTools(fileSearch, fileRepository, currentUser);
        var clockTools = new ClockAgentTools(timeProvider ?? TimeProvider.System);
        var mcpServerTools = new McpServerAgentTools(mcpServers, mcpPolicy, configuration, currentUser, mcpToolHost);

        yield return AIFunctionFactory.Create(clockTools.GetCurrentTime);
        yield return AIFunctionFactory.Create(reminderTools.CreateReminderAsync);
        yield return AIFunctionFactory.Create(reminderTools.ListRemindersAsync);
        yield return AIFunctionFactory.Create(reminderTools.CancelReminderAsync);
        yield return AIFunctionFactory.Create(fileTools.SearchFilesAsync);
        yield return AIFunctionFactory.Create(fileTools.ListFilesAsync);
        yield return AIFunctionFactory.Create(taskTools.ListTasksAsync);
        yield return AIFunctionFactory.Create(taskTools.CancelTaskAsync);
        yield return AIFunctionFactory.Create(watchTools.CreateConditionWatchAsync);
        yield return AIFunctionFactory.Create(watchTools.ListConditionWatchesAsync);
        yield return AIFunctionFactory.Create(watchTools.CancelConditionWatchAsync);
        yield return AIFunctionFactory.Create(memoryTools.SearchMemoryAsync);
        yield return AIFunctionFactory.Create(memoryTools.RememberAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(memoryTools.ForgetMemoryAsync));
        if (!context.IsBackgroundTask)
            yield return AIFunctionFactory.Create(taskTools.CreateTaskAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.ListMcpServersAsync);
        yield return AIFunctionFactory.Create(mcpServerTools.ListMcpConnectionsAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.DiscoverMcpServerToolsAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(mcpServerTools.AddMcpServerAsync));
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
                auditEvents, currentUser, codexProcessLimiter, codexExecutable);
            yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(codingTools.RunCodingTaskAsync));
        }
    }

    private sealed record CodingRepositoryOption(string Name, string Path);
}

internal sealed class CoreAgentContext(
    IDailyBriefingRepository briefings,
    IMemoryService memories,
    MemoryReranker reranker,
    IJarvisTaskRepository tasks,
    IConditionWatchRepository watches,
    IMemoryRecallTracker recalls,
    TimeProvider? timeProvider = null) : IAgentContextContributor
{
    public int Order => 0;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
    [
        new ClockContextProvider(briefings, context.OwnerId, timeProvider ?? TimeProvider.System),
        new PersonalMemoryContextProvider(memories, reranker, context.OwnerId, recalls),
        new ActiveTasksContextProvider(tasks, context.OwnerId, context.ExecutingTaskId),
        new ActiveConditionWatchesContextProvider(watches, context.OwnerId)
    ];
}
