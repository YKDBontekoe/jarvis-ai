using System.Text;
using Jarvis.Agents;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class JarvisTaskActivities(IServiceScopeFactory scopeFactory, ILogger<JarvisTaskActivities> logger)
    : JarvisTaskActivityContract
{
    [Activity("RunJarvisTask")]
    public override async Task<bool> RunTaskAsync(JarvisTaskWorkflowInput input)
    {
        var cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("task.run");
        trace?.SetTag("jarvis.task.id", input.TaskId);
        using var heartbeat = new ActivityHeartbeat(input.TaskId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tasks = services.GetRequiredService<IJarvisTaskRepository>();
        var task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return false;
        if (task.Status == "needs_approval") return true;

        services.GetRequiredService<WorkerCurrentUser>().SetOwner(task.OwnerId);
        await tasks.MarkRunningAsync(task.Id, cancellationToken);
        task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return false;
        if (task.Status == "needs_approval") return true;

        var approvals = services.GetRequiredService<IToolApprovalStore>();
        var conversations = services.GetRequiredService<IConversationStore>();
        var messages = await conversations.GetMessagesAsync(task.ConversationId, cancellationToken);
        var userMessage = messages.SingleOrDefault(x => x.Id == task.UserMessageId);
        if (userMessage is null)
        {
            userMessage = new Message(task.ConversationId, "user", task.Prompt, task.UserMessageId);
            await conversations.AddMessageAsync(userMessage, cancellationToken);
            messages = await conversations.GetMessagesAsync(task.ConversationId, cancellationToken);
        }

        var assistantMessage = messages.SingleOrDefault(x => x.Id == task.ResultMessageId);
        if (assistantMessage is not null)
        {
            await tasks.CompleteAndNotifyAsync(task.Id, assistantMessage.Content, cancellationToken);
            return false;
        }

        var sessionJson = await conversations.GetAgentSessionAsync(task.ConversationId, cancellationToken);
        if (sessionJson is not null &&
            AgentSessionJson.TryGetCompletedAssistantTextAfterUser(sessionJson, userMessage.Content, out var recovered))
        {
            assistantMessage = new Message(task.ConversationId, "assistant", recovered, task.ResultMessageId);
            await conversations.AddMessageAsync(assistantMessage, cancellationToken);
            await tasks.CompleteAndNotifyAsync(task.Id, recovered, cancellationToken);
            return false;
        }

        if (sessionJson is not null &&
            AgentSessionJson.TryGetPendingApprovals(sessionJson, out var pendingFromSession, out var recoveredPreface))
        {
            await PersistTaskApprovalsAsync(conversations, approvals, tasks, task, pendingFromSession,
                recoveredPreface, cancellationToken);
            task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
            return task is { Status: "needs_approval" };
        }

        if (await approvals.HasPendingForTaskAsync(task.Id, task.OwnerId, cancellationToken))
        {
            await tasks.MarkNeedsApprovalAsync(task.Id, cancellationToken);
            task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
            return task is { Status: "needs_approval" };
        }

        var agent = services.GetRequiredService<IJarvisAgent>();
        var answer = new StringBuilder();
        var approvalRequests = new List<AgentToolApprovalRequest>();
        await foreach (var update in agent.StreamReplyAsync(task.ConversationId, userMessage, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.TextDelta)) answer.Append(update.TextDelta);
            if (update.ApprovalRequest is { } request)
                approvalRequests.Add(request);
        }

        if (approvalRequests.Count != 0)
        {
            task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
            if (task is null || task.Status is "completed" or "failed" or "cancelled") return false;
            await PersistTaskApprovalsAsync(conversations, approvals, tasks, task, approvalRequests,
                answer.ToString(), cancellationToken);
            task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
            return task is { Status: "needs_approval" };
        }

        task = await tasks.GetTaskByIdAsync(input.TaskId, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return false;

        var result = answer.ToString();
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("The agent completed without an assistant response.");
        assistantMessage = new Message(task.ConversationId, "assistant", result, task.ResultMessageId);
        await conversations.AddMessageAsync(assistantMessage, cancellationToken);
        try
        {
            await services.GetRequiredService<IConversationMemoryExtractor>()
                .ExtractAndStoreAsync(task.OwnerId, userMessage.Id, userMessage.Content, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task memory extraction failed for task {TaskId}.", task.Id);
        }
        await tasks.CompleteAndNotifyAsync(task.Id, result, cancellationToken);
        return false;
    }

    private async Task PersistTaskApprovalsAsync(IConversationStore conversations,
        IToolApprovalStore approvals, IJarvisTaskRepository tasks, JarvisTaskRecord task,
        IReadOnlyList<AgentToolApprovalRequest> requests, string preface, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preface))
        {
            var existing = await conversations.GetMessagesAsync(task.ConversationId, cancellationToken);
            var last = existing.Count > 0 ? existing[^1] : null;
            if (last is not { Role: "assistant" } || last.Content != preface)
                await conversations.AddMessageAsync(new Message(task.ConversationId, "assistant", preface),
                    cancellationToken);
        }

        foreach (var request in requests)
        {
            try
            {
                await approvals.CreateAsync(task.OwnerId, task.ConversationId, request.RequestId,
                    request.ToolCallId, request.ToolName, request.ArgumentsJson, task.Id, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception,
                    "Task {TaskId} already has approval {RequestId}; continuing recovery.", task.Id,
                    request.RequestId);
            }
        }
        await tasks.MarkNeedsApprovalAsync(task.Id, cancellationToken);
        var current = await tasks.GetTaskByIdAsync(task.Id, cancellationToken);
        if (current is not { Status: "needs_approval" })
            await approvals.CancelIncompleteForTaskAsync(task.Id, task.OwnerId, cancellationToken);
    }

    [Activity("CompleteApprovedJarvisTask")]
    public override async Task CompleteApprovedTaskAsync(JarvisTaskApprovalInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("task.complete_after_approval");
        trace?.SetTag("jarvis.task.id", input.TaskId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var task = await services.GetRequiredService<IJarvisTaskRepository>()
            .GetTaskByIdAsync(input.TaskId, activity.CancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return;
        await services.GetRequiredService<IJarvisTaskRepository>()
            .CompleteAndNotifyAsync(input.TaskId, input.Summary, activity.CancellationToken);
    }

    [Activity("FailJarvisTask")]
    public override async Task FailTaskAsync(JarvisTaskWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        using var trace = JarvisWorkerTelemetry.Source.StartActivity("task.fail");
        trace?.SetTag("jarvis.task.id", input.TaskId);
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJarvisTaskRepository>()
            .FailAsync(input.TaskId, "Jarvis could not complete this task.", activity.CancellationToken);
    }

    [Activity("GetJarvisTaskStatus")]
    public override async Task<string?> GetTaskStatusAsync(JarvisTaskWorkflowInput input)
    {
        var activity = ActivityExecutionContext.Current;
        await using var scope = scopeFactory.CreateAsyncScope();
        var task = await scope.ServiceProvider.GetRequiredService<IJarvisTaskRepository>()
            .GetTaskByIdAsync(input.TaskId, activity.CancellationToken);
        return task?.Status;
    }
}
