using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Jarvis.Agents.Telemetry;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Memory;
using Jarvis.Application.Profiles;
using Jarvis.Api.Errors;
using Jarvis.Api.Telemetry;
using Jarvis.Domain.Conversations;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class AgentRunCoordinator(
    IConversationStore conversations,
    IToolApprovalStore approvals,
    IFileCitationCollector fileCitations,
    IServiceScopeFactory scopes,
    IHubContext<JarvisEventsHub> hub,
    ILogger<AgentRunCoordinator> logger)
{
    private static readonly JsonSerializerOptions CitationJsonOptions = new(JsonSerializerDefaults.Web);
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
        await PublishSafelyAsync(clients, "message.completed", new
        {
            id = assistant.Id,
            role = assistant.Role,
            content = assistant.Content,
            createdAt = assistant.CreatedAt
        }, conversationId, cancellationToken);
        await PublishSafelyAsync(clients, "agent.completed", new { conversationId }, conversationId,
            cancellationToken);
    }

    public async Task<AgentRunOutcome?> TryRecoverPendingApprovalsAsync(Guid ownerId, Guid conversationId,
        Guid? taskId, CancellationToken cancellationToken)
    {
        var session = await conversations.GetAgentSessionAsync(conversationId, cancellationToken);
        if (session is null ||
            !AgentSessionJson.TryGetPendingApprovals(session, out var requests, out var preface))
            return null;

        var clients = hub.Clients.Group(JarvisEventsHub.GroupName(conversationId));
        Message? prefaceMessage = null;
        if (!string.IsNullOrWhiteSpace(preface))
        {
            var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken);
            var last = messages.Count > 0 ? messages[^1] : null;
            if (last is { Role: "assistant" } && last.Content == preface)
            {
                prefaceMessage = last;
            }
            else
            {
                prefaceMessage = new Message(conversationId, "assistant", preface);
                await conversations.AddMessageAsync(prefaceMessage, cancellationToken);
                await PublishSafelyAsync(clients, "message.completed", new
                {
                    id = prefaceMessage.Id,
                    role = prefaceMessage.Role,
                    content = prefaceMessage.Content,
                    createdAt = prefaceMessage.CreatedAt
                }, conversationId, cancellationToken);
            }
        }

        var pending = new List<ToolApprovalRecord>();
        foreach (var request in requests)
        {
            try
            {
                var created = await approvals.CreateAsync(ownerId, conversationId, request.RequestId,
                    request.ToolCallId, request.ToolName, request.ArgumentsJson, taskId, cancellationToken);
                pending.Add(created.Approval);
                await PublishSafelyAsync(clients, "tool.approval_required",
                    ApprovalRealtime.Required(created.Approval), conversationId, cancellationToken);
                if (created.Created)
                    await PublishSafelyAsync(hub.Clients.Group(JarvisEventsHub.OwnerGroupName(ownerId)),
                        "notification.created",
                        new
                        {
                            type = "approval.required",
                            notificationId = created.NotificationId,
                            title = "Approval needed",
                            body = $"Jarvis is waiting for approval to run {created.Approval.ToolName}.",
                            sourceId = created.Approval.Id
                        }, conversationId, cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (pending.Count == 0) return null;
        await PublishSafelyAsync(clients, "agent.waiting_for_approval", new { conversationId },
            conversationId, cancellationToken);
        return new AgentRunOutcome(prefaceMessage, pending);
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
        GenAiTelemetry.TagInvokeAgent(activity, conversationId);
        Sentry.SentrySdk.ConfigureScope(scope => scope.User.Id = ownerId.ToString("D"));

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
            // Callers turn failed runs into chat/channel responses, so these exceptions
            // do not reach the HTTP exception handler. Error logs alone are not issues.
            if (JarvisSentryExceptions.ShouldCapture(exception))
                Sentry.SentrySdk.CaptureException(exception, scope =>
                {
                    scope.SetTag("jarvis.run.kind", kind);
                    scope.SetTag("jarvis.run.outcome", "failed");
                    scope.SetTag("jarvis.conversation.id", conversationId.ToString("D"));
                });
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
        await PublishSafelyAsync(clients, "agent.started", new { conversationId }, conversationId,
            cancellationToken);

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
                        await PublishSafelyAsync(clients, eventName,
                            new { conversationId, tool = toolProgress.ToolName }, conversationId, cancellationToken);

                    if (toolProgress.Phase == "started")
                    {
                        var toolSpan = JarvisDiagnostics.ActivitySource.StartActivity("jarvis.agent.tool");
                        toolSpan?.SetTag("tool.name", toolProgress.ToolName);
                        GenAiTelemetry.TagTool(toolSpan, toolProgress.ToolName);
                        toolSpan?.SetTag(GenAiTelemetry.ConversationId, conversationId.ToString("D"));
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
                    await PublishSafelyAsync(clients, "message.delta",
                        new { conversationId, messageId, delta = update.TextDelta }, conversationId,
                        cancellationToken);
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
            if (!string.IsNullOrWhiteSpace(answer.ToString()))
            {
                preface = new Message(conversationId, "assistant", answer.ToString(), messageId);
                AttachCitations(preface);
                await conversations.AddMessageAsync(preface, cancellationToken);
                await PublishSafelyAsync(clients, "message.completed", MessageCompletedPayload(preface),
                    conversationId, cancellationToken);
            }
            foreach (var request in approvalRequests)
            {
                var created = await approvals.CreateAsync(ownerId, conversationId, request.RequestId,
                    request.ToolCallId, request.ToolName, request.ArgumentsJson, taskId, cancellationToken);
                var approval = created.Approval;
                pending.Add(approval);
                await PublishSafelyAsync(clients, "tool.approval_required", ApprovalRealtime.Required(approval),
                    conversationId, cancellationToken);
                if (created.Created)
                    await PublishSafelyAsync(hub.Clients.Group(JarvisEventsHub.OwnerGroupName(ownerId)),
                        "notification.created",
                        new
                        {
                            type = "approval.required",
                            notificationId = created.NotificationId,
                            title = "Approval needed",
                            body = $"Jarvis is waiting for approval to run {approval.ToolName}.",
                            sourceId = approval.Id
                        }, conversationId, cancellationToken);
            }
            await PublishSafelyAsync(clients, "agent.waiting_for_approval", new { conversationId },
                conversationId, cancellationToken);
            if (memorySourceId is { } approvedFlowSourceId && !string.IsNullOrWhiteSpace(memorySource))
                QueueMemoryExtraction(ownerId, conversationId, approvedFlowSourceId, memorySource);
            return new AgentRunOutcome(preface, pending);
        }

        if (string.IsNullOrWhiteSpace(answer.ToString()))
            throw new InvalidOperationException("The agent completed without an assistant response.");

        var assistantMessage = new Message(conversationId, "assistant", answer.ToString(), messageId);
        AttachCitations(assistantMessage);
        await conversations.AddMessageAsync(assistantMessage, cancellationToken);
        if (memorySourceId is { } sourceMessageId && !string.IsNullOrWhiteSpace(memorySource))
            QueueMemoryExtraction(ownerId, conversationId, sourceMessageId, memorySource);

        await PublishSafelyAsync(clients, "message.completed", MessageCompletedPayload(assistantMessage),
            conversationId, cancellationToken);
        await PublishSafelyAsync(clients, "agent.completed", new { conversationId }, conversationId,
            cancellationToken);
        return new AgentRunOutcome(assistantMessage, []);
    }

    private void AttachCitations(Message message)
    {
        var drained = fileCitations.Drain();
        if (drained.Count == 0) return;
        message.SetCitationsJson(JsonSerializer.Serialize(drained, CitationJsonOptions));
    }

    private static object MessageCompletedPayload(Message message) => new
    {
        id = message.Id,
        role = message.Role,
        content = message.Content,
        createdAt = message.CreatedAt,
        citations = DeserializeCitations(message.CitationsJson)
    };

    private static IReadOnlyList<FileCitation>? DeserializeCitations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<IReadOnlyList<FileCitation>>(json, CitationJsonOptions);
    }

    private async Task PublishSafelyAsync(IClientProxy clients, string eventName, object payload,
        Guid conversationId, CancellationToken cancellationToken)
    {
        try
        {
            await clients.SendAsync(eventName, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not publish {EventName} for conversation {ConversationId}.", eventName, conversationId);
        }
    }

    private void QueueMemoryExtraction(Guid ownerId, Guid conversationId, Guid sourceMessageId, string source)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
                var profiles = scope.ServiceProvider.GetRequiredService<IAssistantProfileService>();
                var conversation = await store.GetAsync(conversationId, ownerId, timeout.Token);
                ProfileBinding? binding = conversation?.ProfileId is { } profileId
                    ? new ProfileBinding(profileId, conversation.ProfileVersion ?? 0,
                        conversation.ProfileSnapshotJson ?? "", conversation.Title)
                    : null;
                var snapshot = await profiles.ResolveSnapshotAsync(binding, ownerId, timeout.Token);
                if (!ProfileScope.AllowsRemember(snapshot) || !ProfileScope.ContributesToLearning(snapshot))
                    return;
                await scope.ServiceProvider.GetRequiredService<IConversationMemoryExtractor>()
                    .ExtractAndStoreAsync(ownerId, sourceMessageId, source, timeout.Token, snapshot.ProfileId);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Memory extraction failed after the agent run for conversation {ConversationId}.", conversationId);
            }
        });
    }

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
