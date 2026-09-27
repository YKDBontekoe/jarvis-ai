using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

public sealed class JarvisAgent(JarvisAgentFactory agentFactory, McpToolHost mcpToolHost,
    IConversationStore conversations, IJarvisTaskRepository tasks, ICurrentUser currentUser) : IJarvisAgent
{
    private static readonly JsonSerializerOptions ArgumentsJsonOptions = new(JsonSerializerDefaults.Web);
    private AIAgent? _agent;

    public async IAsyncEnumerable<AgentStreamEvent> StreamReplyAsync(
        Guid conversationId,
        Message currentUserMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = await GetAgentAsync(conversationId, cancellationToken);
        var sessionJson = await conversations.GetAgentSessionAsync(conversationId, cancellationToken);
        ChatMessage[] input = [new ChatMessage(ChatRole.User, currentUserMessage.Content)];
        if (sessionJson is not null &&
            AgentSessionJson.HasInFlightProgressAfterUser(sessionJson, currentUserMessage.Content))
            input = [];
        else if (sessionJson is not null &&
                 AgentSessionJson.TryAbandonIncompleteTurn(sessionJson, currentUserMessage.Content, out var truncated))
            await conversations.SaveAgentSessionAsync(conversationId, truncated, cancellationToken);

        var session = await LoadSessionAsync(agent, conversationId, cancellationToken);
        await foreach (var update in RunAndSaveAsync(agent, conversationId, input, session, cancellationToken))
            yield return update;
    }

    public async IAsyncEnumerable<AgentStreamEvent> ResumeReplyAsync(
        Guid conversationId,
        ToolApprovalReply approval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = await GetAgentAsync(conversationId, cancellationToken);
        var sessionJson = await conversations.GetAgentSessionAsync(conversationId, cancellationToken);
        ChatMessage[] input;
        if (sessionJson is not null &&
            AgentSessionJson.HasAnsweredApproval(sessionJson, approval.RequestId, approval.ToolCallId))
        {
            input = [];
        }
        else
        {
            var arguments = ToolCallArguments.Parse(approval.ArgumentsJson);
            var functionCall = new FunctionCallContent(approval.ToolCallId, approval.ToolName, arguments);
            var approvalRequest = new ToolApprovalRequestContent(approval.RequestId, functionCall);
            var response = approvalRequest.CreateResponse(approval.Approved,
                approval.Approved ? null : "The user rejected this tool call.");
            input = [new ChatMessage(ChatRole.User, [response])];
        }

        var session = await LoadSessionAsync(agent, conversationId, cancellationToken);
        await foreach (var update in RunAndSaveAsync(agent, conversationId, input, session, cancellationToken))
            yield return update;
    }

    private async Task<AIAgent> GetAgentAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (_agent is not null) return _agent;
        await mcpToolHost.InitializeAsync(cancellationToken);
        var task = await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, cancellationToken);
        return _agent = agentFactory.Create(mcpToolHost.Tools, task?.Id);
    }

    private async Task<AgentSession> LoadSessionAsync(AIAgent agent, Guid conversationId,
        CancellationToken cancellationToken)
    {
        var sessionState = await conversations.GetAgentSessionAsync(conversationId, cancellationToken);
        if (sessionState is null) return await agent.CreateSessionAsync(cancellationToken);

        using var document = JsonDocument.Parse(sessionState);
        return await agent.DeserializeSessionAsync(document.RootElement, cancellationToken: cancellationToken);
    }

    private async IAsyncEnumerable<AgentStreamEvent> RunAndSaveAsync(
        AIAgent agent,
        Guid conversationId,
        IEnumerable<ChatMessage> input,
        AgentSession session,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var activeTools = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var update in agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
        {
            var checkpoint = false;
            var approvalRequests = update.Contents.OfType<ToolApprovalRequestContent>().ToArray();
            var approvalCallIds = approvalRequests
                .Select(request => request.ToolCall)
                .OfType<FunctionCallContent>()
                .Select(call => call.CallId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var text in update.Contents.OfType<TextContent>())
            {
                if (!string.IsNullOrEmpty(text.Text)) yield return new AgentStreamEvent(TextDelta: text.Text);
            }

            foreach (var call in update.Contents.OfType<FunctionCallContent>())
            {
                if (call.InformationalOnly || approvalCallIds.Contains(call.CallId)) continue;
                if (!activeTools.TryAdd(call.CallId, call.Name)) continue;
                yield return new AgentStreamEvent(ToolProgress: new AgentToolProgress(call.CallId, call.Name, "started"));
            }

            foreach (var result in update.Contents.OfType<FunctionResultContent>())
            {
                if (!activeTools.Remove(result.CallId, out var toolName)) continue;
                yield return new AgentStreamEvent(ToolProgress: new AgentToolProgress(result.CallId, toolName,
                    result.Exception is null ? "completed" : "failed"));
                checkpoint = true;
            }

            foreach (var request in approvalRequests)
            {
                if (request.ToolCall is not FunctionCallContent functionCall) continue;
                yield return new AgentStreamEvent(ApprovalRequest: new AgentToolApprovalRequest(
                    request.RequestId,
                    functionCall.CallId,
                    functionCall.Name,
                    JsonSerializer.Serialize(functionCall.Arguments ?? new Dictionary<string, object?>(),
                        ArgumentsJsonOptions)));
                checkpoint = true;
            }

            if (checkpoint)
            {
                var checkpointJson = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
                await conversations.SaveAgentSessionAsync(conversationId, checkpointJson.GetRawText(), cancellationToken);
            }
        }

        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        await conversations.SaveAgentSessionAsync(conversationId, serialized.GetRawText(), cancellationToken);
    }
}
