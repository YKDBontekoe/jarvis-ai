using Jarvis.Agents;
using Jarvis.Api.Endpoints;
using Jarvis.Api.Realtime;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Conversations;

/// <summary>
/// Records an owner's decision on a pending tool call and resumes the paused agent run. Used by the approval
/// API and by messaging channels that accept YES/NO replies.
/// </summary>
public sealed class ApprovalDecisionService(
    IToolApprovalStore approvals,
    IConversationStore conversations,
    IJarvisAgent agent,
    AgentRunCoordinator coordinator,
    ILogger<ApprovalDecisionService> logger,
    IConversationRunLock runLock,
    IJarvisTaskService tasks,
    IJarvisTaskRepository taskRepository,
    ITaskRunAbort taskRunAbort,
    IServiceScopeFactory scopes,
    IHubContext<JarvisEventsHub> hub,
    VoiceBackendSession voice)
{
    private const string CancelledMessage = "This task was cancelled.";

    public async Task<ConversationTurnResult> DecideAsync(Guid ownerId, Guid approvalId, bool approved,
        CancellationToken ct)
    {
        var pending = await approvals.GetActionableAsync(approvalId, ownerId, ct);
        if (pending is null) return new ConversationTurnResult.NotFound();
        await using var runLease = await runLock.AcquireAsync(pending.ConversationId, ct);
        pending = await approvals.GetActionableAsync(approvalId, ownerId, ct);
        if (pending is null)
            return new ConversationTurnResult.Conflict("This approval is already complete or being resumed.");
        if (await conversations.GetAsync(pending.ConversationId, ownerId, ct) is null)
            return new ConversationTurnResult.NotFound();
        if (pending.Status == "pending")
        {
            var earlier = (await approvals.ListActionableForConversationAsync(ownerId, pending.ConversationId, ct))
                .FirstOrDefault(x => x.Status == "pending");
            if (earlier is not null && earlier.Id != pending.Id)
                return new ConversationTurnResult.Conflict("Decide the earlier pending tool call for this conversation first.");
        }
        if (pending.TaskId is { } boundTaskId)
        {
            var task = await taskRepository.GetTaskAsync(boundTaskId, ownerId, ct);
            if (task is null || task.Status != "needs_approval")
                return new ConversationTurnResult.Conflict("This approval is no longer attached to an active task.");
        }
        ToolApprovalRecord? decided;
        if (pending.Status == "pending")
            decided = await approvals.DecideAsync(approvalId, ownerId, approved, ct);
        else if (pending.Approved == approved)
            decided = pending;
        else
            return new ConversationTurnResult.Conflict("Retry must use the decision already recorded for this approval.");
        if (decided is null) return new ConversationTurnResult.Conflict("This approval was already decided.");
        if (!await approvals.TryStartResumeAsync(approvalId, ownerId, ct))
            return new ConversationTurnResult.Conflict("This approval is already being resumed.");

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var abortLease = decided.TaskId is { } resumeTaskId
            ? taskRunAbort.Register(resumeTaskId, abort)
            : null;
        var runCt = abort.Token;
        using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(runCt);
        if (decided.TaskId is { } watchTaskId)
            _ = WatchTaskCancellationAsync(watchTaskId, ownerId, abort, watchCts.Token);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(runCt);
        _ = HeartbeatApprovalResumeAsync(approvalId, ownerId, heartbeatCts.Token);

        try
        {
            if (pending.Status != "pending" &&
                await TryCompleteAlreadyResumedAsync(ownerId, approvalId, decided, runCt) is { } alreadyResumed)
                return alreadyResumed;

            if (VoiceTools.IsVoiceApproval(decided.RequestId))
            {
                var voiceResult = await voice.CompleteApprovalAsync(decided, runCt);
                await approvals.MarkResumeCompletedAsync(approvalId, ownerId, CancellationToken.None);
                return voiceResult.IsError
                    ? new ConversationTurnResult.Failed(voiceResult.Result)
                    : new ConversationTurnResult.Completed(new Jarvis.Domain.Conversations.Message(
                        decided.ConversationId, "assistant", voiceResult.Result));
            }

            var outcome = await coordinator.RunAsync(ownerId, decided.ConversationId,
                agent.ResumeReplyAsync(decided.ConversationId, decided.ToReply(), runCt), null, runCt,
                decided.TaskId);
            if (outcome.PendingApprovals.Count != 0)
            {
                if (!await TaskStillNeedsApprovalAsync(decided.TaskId, ownerId, approvalId))
                    return new ConversationTurnResult.Conflict(CancelledMessage);
                await approvals.MarkResumeCompletedAsync(approvalId, ownerId, CancellationToken.None);
                return new ConversationTurnResult.AwaitingApproval(outcome.PendingApprovals);
            }
            if (decided.Approved == true)
                await tasks.CompleteAfterApprovalAsync(decided.TaskId, ownerId,
                    outcome.AssistantMessage?.Content ?? "The approved task step finished.", CancellationToken.None);
            else
                await tasks.FailAfterRejectedApprovalAsync(decided.TaskId, ownerId,
                    outcome.AssistantMessage?.Content ?? "The tool call was declined.", CancellationToken.None);
            await approvals.MarkResumeCompletedAsync(approvalId, ownerId, CancellationToken.None);
            return new ConversationTurnResult.Completed(outcome.AssistantMessage!);
        }
        catch (OperationCanceledException)
        {
            await approvals.MarkResumeFailedAsync(approvalId, ownerId, CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            if (!abort.IsCancellationRequested)
            {
                // Nobody cancelled the run: a step inside it (an MCP server, the model) timed out. Say so and
                // leave the decision retryable instead of claiming the task was cancelled.
                logger.LogWarning("Tool approval {ApprovalId} timed out while resuming its agent run.", approvalId);
                await EndpointHelpers.PublishAgentFailedAsync(hub, logger, decided.ConversationId,
                    ConversationTurnService.FailureMessage);
                return new ConversationTurnResult.Failed(
                    "Jarvis timed out finishing this tool call. It can be retried from Tool approvals.");
            }
            await EndpointHelpers.PublishAgentFailedAsync(hub, logger, decided.ConversationId, CancelledMessage);
            return new ConversationTurnResult.Conflict(CancelledMessage);
        }
        catch (Exception exception)
        {
            await approvals.MarkResumeFailedAsync(approvalId, ownerId, CancellationToken.None);
            logger.LogError(exception, "Tool approval {ApprovalId} was decided but its agent resume failed.", approvalId);
            var failure = AgentFailureMessage.For(exception);
            await EndpointHelpers.PublishAgentFailedAsync(hub, logger, decided.ConversationId, failure);
            return new ConversationTurnResult.Failed(failure == ConversationTurnService.FailureMessage
                ? "Jarvis could not resume this decided tool call. It can be retried from Tool approvals."
                : failure + " Your decision is saved; retry it from Tool approvals.");
        }
        finally
        {
            try { watchCts.Cancel(); }
            catch (ObjectDisposedException) { }
            try { heartbeatCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task<ConversationTurnResult?> TryCompleteAlreadyResumedAsync(Guid ownerId, Guid approvalId,
        ToolApprovalRecord decided, CancellationToken runCt)
    {
        var messages = await conversations.GetMessagesAsync(decided.ConversationId, runCt);
        var last = messages.Count > 0 ? messages[^1] : null;
        if (last is not { Role: "assistant" } || decided.DecidedAt is not { } decidedAt || last.CreatedAt < decidedAt)
            return null;

        var session = await conversations.GetAgentSessionAsync(decided.ConversationId, runCt);
        if (session is not null &&
            AgentSessionJson.TryGetPendingApprovals(session, out var pendingFromSession, out _))
        {
            foreach (var pendingRequest in pendingFromSession)
            {
                try
                {
                    await approvals.CreateAsync(ownerId, decided.ConversationId, pendingRequest.RequestId,
                        pendingRequest.ToolCallId, pendingRequest.ToolName, pendingRequest.ArgumentsJson,
                        decided.TaskId, runCt);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        if (!await TaskStillNeedsApprovalAsync(decided.TaskId, ownerId, approvalId))
            return new ConversationTurnResult.Conflict(CancelledMessage);
        var remaining = (await approvals.ListActionableForConversationAsync(ownerId, decided.ConversationId,
                CancellationToken.None))
            .Where(x => x.Id != approvalId)
            .ToList();
        if (remaining.Count != 0)
        {
            await approvals.MarkResumeCompletedAsync(approvalId, ownerId, CancellationToken.None);
            return new ConversationTurnResult.AwaitingApproval(remaining);
        }
        if (decided.Approved == true)
            await tasks.CompleteAfterApprovalAsync(decided.TaskId, ownerId, last.Content, CancellationToken.None);
        else
            await tasks.FailAfterRejectedApprovalAsync(decided.TaskId, ownerId, last.Content, CancellationToken.None);
        await approvals.MarkResumeCompletedAsync(approvalId, ownerId, CancellationToken.None);
        return new ConversationTurnResult.Completed(last);
    }

    private async Task<bool> TaskStillNeedsApprovalAsync(Guid? taskId, Guid ownerId, Guid approvalId)
    {
        if (taskId is null) return true;
        var task = await taskRepository.GetTaskAsync(taskId.Value, ownerId, CancellationToken.None);
        if (task is { Status: "needs_approval" }) return true;
        await approvals.CancelIncompleteForTaskAsync(taskId.Value, ownerId, CancellationToken.None);
        await approvals.MarkResumeFailedAsync(approvalId, ownerId, CancellationToken.None);
        return false;
    }

    private async Task HeartbeatApprovalResumeAsync(Guid approvalId, Guid ownerId, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IToolApprovalStore>()
                        .HeartbeatResumeAsync(approvalId, ownerId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Approval resume heartbeat failed for {ApprovalId}.", approvalId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WatchTaskCancellationAsync(Guid taskId, Guid ownerId, CancellationTokenSource abort,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var task = await scope.ServiceProvider.GetRequiredService<IJarvisTaskRepository>()
                        .GetTaskAsync(taskId, ownerId, cancellationToken);
                    if (task is null || task.Status == "cancelled")
                    {
                        try { abort.Cancel(); }
                        catch (ObjectDisposedException) { }
                        return;
                    }
                    if (task.Status is "completed" or "failed")
                        return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Task cancellation watch failed for {TaskId}.", taskId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
