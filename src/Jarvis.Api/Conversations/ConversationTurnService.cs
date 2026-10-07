using Jarvis.Api.Endpoints;
using Jarvis.Api.Realtime;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Conversations;

public abstract record ConversationTurnResult
{
    public sealed record NotFound : ConversationTurnResult;
    public sealed record Conflict(string Message) : ConversationTurnResult;
    public sealed record AwaitingApproval(IReadOnlyList<ToolApprovalRecord> Approvals) : ConversationTurnResult;
    public sealed record Completed(Message Message) : ConversationTurnResult;
    public sealed record Failed(string Message) : ConversationTurnResult;
}

/// <summary>
/// Runs one user turn through the shared conversation, approval, and recovery rules. HTTP chat,
/// messaging channels, and A2A enter the agent through this service so their behavior cannot drift.
/// Realtime voice calls the same tools on the backend over MCP instead of converting each utterance.
/// </summary>
public sealed class ConversationTurnService(
    IConversationStore store,
    IConversationRunLock runLock,
    IJarvisTaskRepository tasks,
    IToolApprovalStore approvals,
    IJarvisAgent agent,
    AgentRunCoordinator coordinator,
    IHubContext<JarvisEventsHub> hub,
    ILogger<ConversationTurnService> logger,
    Jarvis.Application.Learning.ILearningRecorder learning)
{
    public const string FailureMessage = "Jarvis could not complete this response.";

    public async Task<ConversationTurnResult> SendAsync(Guid ownerId, Guid conversationId, string content,
        CancellationToken cancellationToken, Func<string, CancellationToken, Task>? onTextDelta = null,
        Func<CancellationToken, Task>? beforeRun = null, IReadOnlyList<MessageAttachment>? attachments = null)
    {
        var conversation = await store.GetAsync(conversationId, ownerId, cancellationToken);
        if (conversation is null) return new ConversationTurnResult.NotFound();
        if (await tasks.GetTaskByConversationIdAsync(conversationId, ownerId, cancellationToken) is not null)
            return new ConversationTurnResult.Conflict("Long-running task sessions are managed from the Tasks section.");

        await using var runLease = await runLock.AcquireAsync(conversationId, cancellationToken);
        var existingMessages = await store.GetMessagesAsync(conversationId, cancellationToken);
        var lastMessage = existingMessages.Count > 0 ? existingMessages[^1] : null;
        var attachmentsJson = MessageAttachments.Serialize(attachments);
        var isSameUserTurn = lastMessage is { Role: "user" } && lastMessage.Content == content &&
                             lastMessage.AttachmentsJson == attachmentsJson;
        var pendingApprovals = await approvals.ListActionableForConversationAsync(ownerId, conversationId,
            cancellationToken);
        if (pendingApprovals.Count > 0)
        {
            if (!isSameUserTurn)
                return new ConversationTurnResult.Conflict("Decide the pending tool call for this conversation first.");
            var stillPending = pendingApprovals.Where(x => x.Status == "pending").ToList();
            if (stillPending.Count > 0) return new ConversationTurnResult.AwaitingApproval(stillPending);
            return new ConversationTurnResult.Conflict("A previous tool decision is still finishing for this conversation.");
        }

        if (beforeRun is not null) await beforeRun(cancellationToken);
        var userMessage = isSameUserTurn
            ? lastMessage!
            : new Message(conversationId, "user", content, attachmentsJson: attachmentsJson);
        if (!isSameUserTurn)
        {
            await store.AddMessageAsync(userMessage, cancellationToken);
            // A message that pushes back on the reply right before it is worth knowing about; only the fact is kept.
            if (lastMessage is { Role: "assistant" } && Jarvis.Application.Learning.CorrectionDetector.LooksLikeCorrection(content))
                learning.RecordSignal(ownerId, conversationId, Jarvis.Application.Learning.LearningSignalKinds.Correction,
                    userMessage.Id);
        }
        if (ReferenceEquals(userMessage, lastMessage) &&
            await coordinator.TryRecoverCompletedAssistantAsync(conversationId, content, cancellationToken)
                is { } recovered)
        {
            await coordinator.PublishRecoveredAssistantAsync(conversationId, recovered, cancellationToken);
            return new ConversationTurnResult.Completed(recovered);
        }
        if (ReferenceEquals(userMessage, lastMessage) &&
            await coordinator.TryRecoverPendingApprovalsAsync(ownerId, conversationId, null, cancellationToken)
                is { PendingApprovals.Count: > 0 } recoveredApprovals)
            return new ConversationTurnResult.AwaitingApproval(recoveredApprovals.PendingApprovals);

        try
        {
            var outcome = await RunWithOneRetryAsync(ownerId, conversationId, userMessage, userMessage.Content,
                userMessage.Id, onTextDelta, cancellationToken);
            return outcome.PendingApprovals.Count != 0
                ? new ConversationTurnResult.AwaitingApproval(outcome.PendingApprovals)
                : new ConversationTurnResult.Completed(outcome.AssistantMessage!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent run failed for conversation {ConversationId}", conversationId);
            var failure = AgentFailureMessage.For(exception);
            await EndpointHelpers.PublishAgentFailedAsync(hub, logger, conversationId, failure);
            return new ConversationTurnResult.Failed(failure);
        }
    }

    /// <summary>
    /// Answers the last user message again: removes the last reply from the transcript and the agent
    /// history, then runs the turn anew. Refused while approvals are open or when the reply used tools,
    /// so actions such as reminders or messages are never repeated.
    /// </summary>
    public async Task<ConversationTurnResult> RegenerateAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await store.GetAsync(conversationId, ownerId, cancellationToken);
        if (conversation is null) return new ConversationTurnResult.NotFound();
        if (await tasks.GetTaskByConversationIdAsync(conversationId, ownerId, cancellationToken) is not null)
            return new ConversationTurnResult.Conflict("Long-running task sessions are managed from the Tasks section.");

        await using var runLease = await runLock.AcquireAsync(conversationId, cancellationToken);
        var messages = await store.GetMessagesAsync(conversationId, cancellationToken);
        if (messages.Count < 2 || messages[^1] is not { Role: "assistant" } reply ||
            messages[^2] is not { Role: "user" } userMessage)
            return new ConversationTurnResult.Conflict("There is no reply to try again.");
        if ((await approvals.ListActionableForConversationAsync(ownerId, conversationId, cancellationToken))
            .Count > 0)
            return new ConversationTurnResult.Conflict("Decide the pending tool call for this conversation first.");

        var session = await store.GetAgentSessionAsync(conversationId, cancellationToken);
        if (session is not null)
        {
            switch (AgentSessionJson.TryDropLastTurnForRegenerate(session, out var truncated))
            {
                case RegenerateTruncation.UsedTools:
                    return new ConversationTurnResult.Conflict(
                        "This reply used tools, so Jarvis cannot redo it without repeating those actions. Ask again instead.");
                case RegenerateTruncation.Dropped:
                    await store.SaveAgentSessionAsync(conversationId, truncated, cancellationToken);
                    break;
                default:
                    return new ConversationTurnResult.Conflict("There is no reply to try again.");
            }
        }
        // The reply is deleted next, so the signal is written first and refers to it by id only.
        learning.RecordSignal(ownerId, conversationId, Jarvis.Application.Learning.LearningSignalKinds.Regenerate,
            reply.Id);
        await store.DeleteMessageAsync(conversationId, reply.Id, cancellationToken);

        try
        {
            var outcome = await RunWithOneRetryAsync(ownerId, conversationId, userMessage, null, null, null,
                cancellationToken);
            return outcome.PendingApprovals.Count != 0
                ? new ConversationTurnResult.AwaitingApproval(outcome.PendingApprovals)
                : new ConversationTurnResult.Completed(outcome.AssistantMessage!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent regenerate failed for conversation {ConversationId}", conversationId);
            var failure = AgentFailureMessage.For(exception);
            await EndpointHelpers.PublishAgentFailedAsync(hub, logger, conversationId, failure);
            return new ConversationTurnResult.Failed(failure);
        }
    }

    /// <summary>
    /// Runs the agent and, when it fails before producing anything the owner or a tool could have seen, once more.
    /// See <see cref="RetryWhenNothingHappenedAsync{T}"/>.
    /// </summary>
    private Task<AgentRunOutcome> RunWithOneRetryAsync(Guid ownerId, Guid conversationId, Message userMessage,
        string? memorySource, Guid? memorySourceId, Func<string, CancellationToken, Task>? onTextDelta,
        CancellationToken cancellationToken) =>
        RetryWhenNothingHappenedAsync(
            produced => coordinator.RunAsync(ownerId, conversationId,
                Observe(agent.StreamReplyAsync(conversationId, userMessage, cancellationToken), produced),
                memorySource, cancellationToken, memorySourceId: memorySourceId, onTextDelta: onTextDelta),
            exception => logger.LogWarning(exception,
                "Agent run failed before producing output for conversation {ConversationId}; trying once more.",
                conversationId),
            cancellationToken);

    /// <summary>
    /// Calls <paramref name="attempt"/> and, when it throws a retryable failure (a model process that died or timed
    /// out) before calling the <c>produced</c> callback, calls it once more. Nothing has been shown, said or done at
    /// that point, so repeating it cannot duplicate an action. Any failure after the first output, a second failure,
    /// and non-retryable failures such as being signed out propagate unchanged.
    /// </summary>
    internal static async Task<T> RetryWhenNothingHappenedAsync<T>(Func<Action, Task<T>> attempt,
        Action<Exception>? onRetry, CancellationToken cancellationToken)
    {
        for (var tries = 0; ; tries++)
        {
            var anything = false;
            try
            {
                return await attempt(() => anything = true);
            }
            catch (Exception exception) when (tries == 0 && !anything && !cancellationToken.IsCancellationRequested &&
                                              AgentFailureMessage.IsRetryable(exception))
            {
                onRetry?.Invoke(exception);
            }
        }
    }

    private static async IAsyncEnumerable<AgentStreamEvent> Observe(IAsyncEnumerable<AgentStreamEvent> source,
        Action produced)
    {
        await foreach (var item in source)
        {
            produced();
            yield return item;
        }
    }
}
