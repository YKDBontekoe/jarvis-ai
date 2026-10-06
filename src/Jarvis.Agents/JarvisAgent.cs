using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Agents.ModelProviders;
using Jarvis.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

public sealed class JarvisAgent(JarvisAgentFactory agentFactory, IChatClientResolver chatClients, McpToolHost mcpToolHost,
    IConversationStore conversations, IJarvisTaskRepository tasks, IAssistantProfileService profiles,
    IOwnerSettingsStore settings, ICurrentUser currentUser, IFileService files,
    IStandingApprovalService standingApprovals, IApprovalPolicy approvalPolicy) : IJarvisAgent
{
    private const int MaxAutomaticApprovalsPerTurn = 16;
    private bool _backgroundTask;
    private static readonly JsonSerializerOptions ArgumentsJsonOptions = new(JsonSerializerDefaults.Web);
    private AIAgent? _agent;

    public async IAsyncEnumerable<AgentStreamEvent> StreamReplyAsync(
        Guid conversationId,
        Message currentUserMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = await GetAgentAsync(conversationId, cancellationToken);
        var sessionJson = await conversations.GetAgentSessionAsync(conversationId, cancellationToken);
        ChatMessage[] input = [await BuildUserMessageAsync(currentUserMessage, cancellationToken)];
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

    /// <summary>
    /// The user's text plus any photos they sent, loaded from their own files. A photo that is gone or too
    /// large becomes a short note so the turn still runs.
    /// </summary>
    private async Task<ChatMessage> BuildUserMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var attachments = MessageAttachments.Parse(message.AttachmentsJson);
        if (attachments.Count == 0) return new ChatMessage(ChatRole.User, message.Content);

        List<AIContent> contents = [new TextContent(message.Content)];
        foreach (var attachment in attachments.Take(MessageAttachments.MaxPerMessage))
        {
            var opened = await files.OpenReadAsync(attachment.FileId, currentUser.OwnerId, cancellationToken);
            if (opened is not { } file || !MessageAttachments.IsSupportedImage(file.File.ContentType) ||
                file.File.SizeBytes > MessageAttachments.MaxImageBytes)
            {
                if (opened is { } unused) await unused.Content.DisposeAsync();
                contents.Add(new TextContent("[A photo the user attached is no longer available.]"));
                continue;
            }
            await using var stream = file.Content;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > MessageAttachments.MaxImageBytes)
            {
                contents.Add(new TextContent("[A photo the user attached is too large to show.]"));
                continue;
            }
            contents.Add(new DataContent(buffer.ToArray(), file.File.ContentType));
        }
        return new ChatMessage(ChatRole.User, contents);
    }

    private async Task<AIAgent> GetAgentAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (_agent is not null) return _agent;
        await mcpToolHost.InitializeAsync(cancellationToken);
        var ownerId = currentUser.OwnerId;
        var conversation = await conversations.GetAsync(conversationId, ownerId, cancellationToken);
        var task = await tasks.GetTaskByConversationIdAsync(conversationId, ownerId, cancellationToken);
        _backgroundTask = task is not null;
        var snapshot = await ResolveProfileAsync(conversation, ownerId, cancellationToken);
        var purpose = ResolvePurpose(task is not null, snapshot);
        var ownerModels = await settings.GetAsync<ModelSettings>(ownerId, SettingsSections.Models, cancellationToken)
                          ?? ModelSettings.Default;
        var chatClient = await chatClients.GetChatClientAsync(ownerId, purpose, cancellationToken,
            ProfileScope.OverlayModels(ownerModels, snapshot));
        return _agent = agentFactory.Create(chatClient, mcpToolHost.Tools,
            new AgentBuildContext(ownerId, task?.Id, conversationId, snapshot));
    }

    private async Task<AssistantProfileSnapshot> ResolveProfileAsync(
        Jarvis.Domain.Conversations.Conversation? conversation, Guid ownerId, CancellationToken cancellationToken)
    {
        ProfileBinding? binding = conversation?.ProfileId is { } profileId
            ? new ProfileBinding(profileId, conversation.ProfileVersion ?? 0,
                conversation.ProfileSnapshotJson ?? "", conversation.Title)
            : null;
        var snapshot = await profiles.ResolveSnapshotAsync(binding, ownerId, cancellationToken);
        if (conversation is not null && conversation.ProfileSnapshotJson is null)
        {
            var captured = await profiles.CaptureBindingAsync(ownerId, snapshot.ProfileId, cancellationToken);
            await conversations.BindProfileAsync(conversation.Id, ownerId, captured, cancellationToken);
            snapshot = ProfileJson.Deserialize(captured.SnapshotJson) ?? snapshot;
        }
        return snapshot;
    }

    private static ModelPurpose ResolvePurpose(bool isTask, AssistantProfileSnapshot snapshot) =>
        snapshot.ModelClass switch
        {
            Jarvis.Domain.Profiles.ProfileModelClasses.Fast => ModelPurpose.Background,
            Jarvis.Domain.Profiles.ProfileModelClasses.Reasoning => ModelPurpose.Reasoning,
            Jarvis.Domain.Profiles.ProfileModelClasses.Chat => ModelPurpose.Chat,
            _ => isTask ? ModelPurpose.Reasoning : ModelPurpose.Chat
        };

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
        var streamCompleted = false;
        var nextInput = input;
        var automaticApprovals = 0;
        try
        {
            while (true)
            {
                var granted = new List<ToolApprovalRequestContent>();
                await foreach (var update in agent.RunStreamingAsync(nextInput, session,
                                   cancellationToken: cancellationToken))
                {
                    var checkpoint = false;
                    var approvalRequests = update.Contents.OfType<ToolApprovalRequestContent>().ToArray();
                    var approvalCallIds = approvalRequests
                        .Select(request => request.ToolCall)
                        .OfType<FunctionCallContent>()
                        .Select(call => call.CallId)
                        .ToHashSet(StringComparer.Ordinal);

                    if (NativeToolProgress.Read(update) is { } native)
                    {
                        if (native.Phase == "started" ? activeTools.TryAdd(native.ToolCallId, native.ToolName)
                                : activeTools.Remove(native.ToolCallId))
                            yield return new AgentStreamEvent(ToolProgress: native);
                    }

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

                    var calls = approvalRequests
                        .Where(request => request.ToolCall is FunctionCallContent)
                        .ToArray();
                    // One call per turn. A standing grant answers it here so the tool runs in this same turn.
                    if (calls.Length == 1 && automaticApprovals < MaxAutomaticApprovalsPerTurn &&
                        await AutoApprovedAsync(calls[0], conversationId, cancellationToken))
                    {
                        granted.Add(calls[0]);
                        checkpoint = true;
                    }
                    else
                    {
                        foreach (var request in calls)
                        {
                            if (request.ToolCall is not FunctionCallContent functionCall) continue;
                            yield return new AgentStreamEvent(ApprovalRequest: ToApprovalRequest(functionCall, request.RequestId));
                            checkpoint = true;
                        }
                    }

                    if (checkpoint)
                        await SaveSessionAsync(agent, conversationId, session, cancellationToken);
                }

                if (granted.Count == 0) break;
                automaticApprovals += granted.Count;
                nextInput =
                [
                    new ChatMessage(ChatRole.User, granted.Select(request =>
                        request.CreateResponse(true)).ToArray())
                ];
            }

            streamCompleted = true;
        }
        finally
        {
            try
            {
                await SaveSessionAsync(agent, conversationId, session, CancellationToken.None, stripImages: true);
            }
            catch (Exception) when (!streamCompleted)
            {
            }
        }
    }

    /// <summary>
    /// Answers an approval request without asking when the owner allowed it: first a standing per-category grant, then
    /// the autonomy policy (read-only tools, and reversible changes inside background tasks).
    /// </summary>
    private async Task<bool> AutoApprovedAsync(ToolApprovalRequestContent request, Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (request.ToolCall is not FunctionCallContent functionCall) return false;
        try
        {
            var category = ApprovalCategories.Resolve(functionCall.Name, SerializeArguments(functionCall));
            if (category.CanRemember &&
                await standingApprovals.IsGrantedAsync(currentUser.OwnerId, category.Key, cancellationToken))
            {
                await standingApprovals.RecordAutomaticUseAsync(currentUser.OwnerId, functionCall.Name, category,
                    conversationId, cancellationToken);
                return true;
            }

            // A server's read-only claim only counts for its own tools, never for a name that is a built-in.
            var declaredReadOnly = mcpToolHost.IsReadOnlyTool(functionCall.Name) &&
                                   ToolRiskPolicy.Classify(functionCall.Name) == ToolRisk.Unknown;
            var decision = await approvalPolicy.EvaluateAsync(new ApprovalPolicyRequest(currentUser.OwnerId,
                functionCall.Name, _backgroundTask, declaredReadOnly, conversationId), cancellationToken);
            return decision.AutoApprove;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private AgentToolApprovalRequest ToApprovalRequest(FunctionCallContent functionCall, string requestId) =>
        new(requestId, functionCall.CallId, functionCall.Name, SerializeArguments(functionCall));

    private string SerializeArguments(FunctionCallContent functionCall) =>
        JsonSerializer.Serialize(functionCall.Arguments ?? new Dictionary<string, object?>(), ArgumentsJsonOptions);

    /// <param name="stripImages">
    /// True once the run has ended: photos were seen in their own turn, so the stored history keeps only a
    /// note in their place. Checkpoints during a run keep them for the rest of that turn.
    /// </param>
    private async Task SaveSessionAsync(AIAgent agent, Guid conversationId, AgentSession session,
        CancellationToken cancellationToken, bool stripImages = false)
    {
        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        var state = serialized.GetRawText();
        if (stripImages) state = AgentSessionJson.StripImageData(state);
        await conversations.SaveAgentSessionAsync(conversationId, state, cancellationToken);
    }
}
