using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Api.Telemetry;
using Jarvis.Domain.Conversations;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class AgentRunCoordinator(
    IConversationStore conversations,
    IToolApprovalStore approvals,
    IConversationMemoryExtractor memoryExtractor,
    IHubContext<JarvisEventsHub> hub,
    ILogger<AgentRunCoordinator> logger)
{
    public async Task<Message?> TryRecoverCompletedAssistantAsync(Guid conversationId, string userContent,
        CancellationToken cancellationToken)
    {
        var session = await conversations.GetAgentSessionAsync(conversationId, cancellationToken);
        if (session is null ||
            !AgentSessionJson.TryGetCompletedAssistantTextAfterUser(session, userContent, out var recovered))
            return null;

        var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken);
        var last = messages.Count > 0 ? messages[^1] : null;
        if (last is { Role: "assistant" } && last.Content == recovered)
            return last;
        if (last is not { Role: "user" } || last.Content != userContent)
            return null;

        var assistant = new Message(conversationId, "assistant", recovered);
        await conversations.AddMessageAsync(assistant, cancellationToken);
        return assistant;
    }

    public async Task PublishRecoveredAssistantAsync(Guid conversationId, Message assistant,
        CancellationToken cancellationToken)
    {
        var clients = hub.Clients.Group(JarvisEventsHub.GroupName(conversationId));
        await clients.SendAsync("message.completed", new
        {
            id = assistant.Id,
            role = assistant.Role,
            content = assistant.Content,
            createdAt = assistant.CreatedAt
        }, cancellationToken);
        await clients.SendAsync("agent.completed", new { conversationId }, cancellationToken);
    }

    public async Task<AgentRunOutcome> RunAsync(Guid ownerId, Guid conversationId,
        IAsyncEnumerable<AgentStreamEvent> events, string? memorySource, CancellationToken cancellationToken,
        Guid? taskId = null, Guid? memorySourceId = null,
        Func<string, CancellationToken, Task>? onTextDelta = null)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var kind = taskId is null ? "interactive" : "durable_task";
        var outcome = "failed";
        using var activity = JarvisDiagnostics.ActivitySource.StartActivity("jarvis.agent.run");
        activity?.SetTag("jarvis.run.kind", kind);

        try
        {
            var result = await RunCoreAsync(ownerId, conversationId, events, memorySource, cancellationToken,
                taskId, memorySourceId, onTextDelta, activity, startedAt);
            outcome = result.PendingApprovals.Count > 0 ? "approval_required" : "completed";
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            activity?.SetTag("jarvis.run.outcome", outcome);
            var tags = new TagList
            {
                { "run.kind", kind },
                { "run.outcome", outcome }
            };
            JarvisDiagnostics.AgentRunDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        }
    }

    private async Task<AgentRunOutcome> RunCoreAsync(Guid ownerId, Guid conversationId,
        IAsyncEnumerable<AgentStreamEvent> events, string? memorySource, CancellationToken cancellationToken,
        Guid? taskId, Guid? memorySourceId, Func<string, CancellationToken, Task>? onTextDelta,
        Activity? activity, long runStartedAt)
    {
        var messageId = Guid.CreateVersion7();
        var answer = new StringBuilder();
        var pending = new List<ToolApprovalRecord>();
        var approvalRequests = new List<AgentToolApprovalRequest>();
        var activeToolSpans = new Dictionary<string, (Activity? Span, long StartedAt)>(StringComparer.Ordinal);
        var firstTextTokenSeen = false;
        var clients = hub.Clients.Group(JarvisEventsHub.GroupName(conversationId));
        await clients.SendAsync("agent.started", new { conversationId }, cancellationToken);

        try
        {
            await foreach (var update in events.WithCancellation(cancellationToken))
            {
                if (update.ToolProgress is { ToolName.Length: > 0 } toolProgress)
                {
                    var eventName = toolProgress.Phase switch
                    {
                        "started" => "tool.started",
                        "completed" => "tool.completed",
                        "failed" => "tool.failed",
                        _ => null
                    };
                    if (eventName is not null)
                        await clients.SendAsync(eventName, new { conversationId, tool = toolProgress.ToolName }, cancellationToken);

                    if (toolProgress.Phase == "started")
                    {
                        var toolSpan = JarvisDiagnostics.ActivitySource.StartActivity("jarvis.agent.tool");
                        toolSpan?.SetTag("tool.name", toolProgress.ToolName);
                        activeToolSpans.TryAdd(toolProgress.ToolCallId, (toolSpan, Stopwatch.GetTimestamp()));
                    }
                    else if (toolProgress.Phase is "completed" or "failed")
                    {
                        var tags = new TagList
                        {
                            { "tool.name", toolProgress.ToolName },
                            { "tool.outcome", toolProgress.Phase }
                        };
                        JarvisDiagnostics.ToolCalls.Add(1, tags);
                        if (activeToolSpans.Remove(toolProgress.ToolCallId, out var toolTiming))
                        {
                            if (toolProgress.Phase == "failed")
                                toolTiming.Span?.SetStatus(ActivityStatusCode.Error, "tool_failed");
                            JarvisDiagnostics.ToolDuration.Record(
                                Stopwatch.GetElapsedTime(toolTiming.StartedAt).TotalMilliseconds, tags);
                            toolTiming.Span?.Dispose();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(update.TextDelta))
                {
                    if (!firstTextTokenSeen)
                    {
                        firstTextTokenSeen = true;
                        var elapsed = Stopwatch.GetElapsedTime(runStartedAt).TotalMilliseconds;
                        JarvisDiagnostics.AgentTimeToFirstToken.Record(elapsed,
                            new KeyValuePair<string, object?>("run.kind", taskId is null ? "interactive" : "durable_task"));
                        activity?.SetTag("jarvis.time_to_first_token_ms", elapsed);
                    }
                    answer.Append(update.TextDelta);
                    await clients.SendAsync("message.delta", new { conversationId, messageId, delta = update.TextDelta }, cancellationToken);
                    if (onTextDelta is not null)
                        await onTextDelta(update.TextDelta, cancellationToken);
                }

                if (update.ApprovalRequest is { } request)
                    approvalRequests.Add(request);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CloseOutstandingToolSpans(activeToolSpans, "cancelled");
            throw;
        }
        catch
        {
            CloseOutstandingToolSpans(activeToolSpans, "failed");
            throw;
        }
        CloseOutstandingToolSpans(activeToolSpans, "incomplete");

        if (approvalRequests.Count != 0)
        {
            Message? preface = null;
            if (answer.Length > 0)
            {
                preface = new Message(conversationId, "assistant", answer.ToString(), messageId);
                await conversations.AddMessageAsync(preface, cancellationToken);
                await clients.SendAsync("message.completed", new
                {
                    id = preface.Id,
                    role = preface.Role,
                    content = preface.Content,
                    createdAt = preface.CreatedAt
                }, cancellationToken);
            }
            foreach (var request in approvalRequests)
            {
                var created = await approvals.CreateAsync(ownerId, conversationId, request.RequestId,
                    request.ToolCallId, request.ToolName, request.ArgumentsJson, taskId, cancellationToken);
                var approval = created.Approval;
                pending.Add(approval);
                await clients.SendAsync("tool.approval_required", ToApprovalEvent(approval), cancellationToken);
                if (created.Created)
                    await hub.Clients.Group(JarvisEventsHub.OwnerGroupName(ownerId)).SendAsync("notification.created",
                        new
                        {
                        type = "approval.required",
                        notificationId = created.NotificationId,
                        title = "Approval needed",
                            body = $"Jarvis is waiting for approval to run {approval.ToolName}.",
                            sourceId = approval.Id
                        }, cancellationToken);
            }
            await clients.SendAsync("agent.waiting_for_approval", new { conversationId }, cancellationToken);
            if (memorySourceId is { } approvedFlowSourceId && !string.IsNullOrWhiteSpace(memorySource))
                await ExtractMemorySafelyAsync(ownerId, conversationId, approvedFlowSourceId, memorySource,
                    cancellationToken);
            return new AgentRunOutcome(preface, pending);
        }

        var assistantMessage = new Message(conversationId, "assistant", answer.ToString(), messageId);
        await conversations.AddMessageAsync(assistantMessage, cancellationToken);
        if (memorySourceId is { } sourceMessageId && !string.IsNullOrWhiteSpace(memorySource))
            await ExtractMemorySafelyAsync(ownerId, conversationId, sourceMessageId, memorySource, cancellationToken);

        await clients.SendAsync("message.completed", new
        {
            id = assistantMessage.Id,
            role = assistantMessage.Role,
            content = assistantMessage.Content,
            createdAt = assistantMessage.CreatedAt
        }, cancellationToken);
        await clients.SendAsync("agent.completed", new { conversationId }, cancellationToken);
        return new AgentRunOutcome(assistantMessage, []);
    }

    private async Task ExtractMemorySafelyAsync(Guid ownerId, Guid conversationId, Guid sourceMessageId,
        string source, CancellationToken cancellationToken)
    {
        try
        {
            await memoryExtractor.ExtractAndStoreAsync(ownerId, sourceMessageId, source, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Memory extraction failed after the agent run for conversation {ConversationId}.", conversationId);
        }
    }

    private static object ToApprovalEvent(ToolApprovalRecord approval) => new
    {
        approval.Id,
        approval.ConversationId,
        approval.ToolName,
        approval.ArgumentsJson,
        approval.CreatedAt
    };

    private static void CloseOutstandingToolSpans(
        Dictionary<string, (Activity? Span, long StartedAt)> activeToolSpans, string outcome)
    {
        foreach (var toolTiming in activeToolSpans.Values)
        {
            var toolName = toolTiming.Span?.GetTagItem("tool.name") as string ?? "unknown";
            toolTiming.Span?.SetStatus(ActivityStatusCode.Error, outcome);
            var tags = new TagList
            {
                { "tool.name", toolName },
                { "tool.outcome", outcome }
            };
            JarvisDiagnostics.ToolCalls.Add(1, tags);
            JarvisDiagnostics.ToolDuration.Record(
                Stopwatch.GetElapsedTime(toolTiming.StartedAt).TotalMilliseconds, tags);
            toolTiming.Span?.Dispose();
        }
        activeToolSpans.Clear();
    }
}

public sealed record AgentRunOutcome(Message? AssistantMessage, IReadOnlyList<ToolApprovalRecord> PendingApprovals);
