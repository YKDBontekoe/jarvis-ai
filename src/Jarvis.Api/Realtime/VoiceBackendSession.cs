using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Api.Security;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Domain.Conversations;
using Jarvis.Mcp;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;

namespace Jarvis.Api.Realtime;

public sealed class VoiceBackendSession(
    IServiceScopeFactory scopes,
    IHubContext<JarvisEventsHub> hub,
    ILogger<VoiceBackendSession> logger)
{
    public async Task<VoiceSessionSnapshot> GetSessionAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateOwnerScope(ownerId);
        var context = new AgentBuildContext(ownerId, null, conversationId);
        var mcp = scope.ServiceProvider.GetRequiredService<McpToolHost>();
        await mcp.InitializeAsync(cancellationToken);
        var functions = scope.ServiceProvider.GetRequiredService<JarvisAgentFactory>()
            .CollectFunctions(mcp.Tools, context);
        var instructions = await scope.ServiceProvider.GetRequiredService<VoiceSessionContext>()
            .BuildInstructionsAsync(ownerId, cancellationToken);
        return new VoiceSessionSnapshot(instructions, functions.Select(VoiceTools.Describe).ToArray());
    }

    public async Task<VoiceToolInvocation> InvokeAsync(Guid ownerId, Guid conversationId, string toolName,
        string? argumentsJson, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateOwnerScope(ownerId);
        var function = await ResolveFunctionAsync(scope.ServiceProvider, ownerId, conversationId, toolName,
            cancellationToken);
        if (function is null)
            return new VoiceToolInvocation($"Unknown Jarvis tool '{toolName}'.", true, false);

        if (VoiceTools.RequiresApproval(function))
        {
            var approval = await CreateApprovalAsync(scope.ServiceProvider, ownerId, conversationId, function.Name,
                argumentsJson ?? "{}", cancellationToken);
            return new VoiceToolInvocation(VoiceTools.ApprovalNeeded, false, true, approval.Id);
        }

        return await ExecuteAsync(scope.ServiceProvider, ownerId, conversationId, function, argumentsJson,
            cancellationToken);
    }

    public async Task<VoiceToolInvocation> CompleteApprovalAsync(ToolApprovalRecord decided,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateOwnerScope(decided.OwnerId);
        if (decided.Approved != true)
        {
            var declined = "The user declined that action in the Jarvis app.";
            await PersistAsync(scope.ServiceProvider, decided.OwnerId, decided.ConversationId, "assistant", declined,
                cancellationToken);
            return new VoiceToolInvocation(declined, false, false, decided.Id);
        }

        var function = await ResolveFunctionAsync(scope.ServiceProvider, decided.OwnerId, decided.ConversationId,
            decided.ToolName, cancellationToken);
        if (function is null)
            return new VoiceToolInvocation($"Unknown Jarvis tool '{decided.ToolName}'.", true, false, decided.Id);

        var result = await ExecuteAsync(scope.ServiceProvider, decided.OwnerId, decided.ConversationId,
            VoiceTools.UnwrapApprovals(function), decided.ArgumentsJson, cancellationToken);
        await PersistAsync(scope.ServiceProvider, decided.OwnerId, decided.ConversationId, "assistant",
            result.Result, cancellationToken);
        return result with { ApprovalId = decided.Id };
    }

    public async Task PersistUtteranceAsync(Guid ownerId, Guid conversationId, string role, string text,
        CancellationToken cancellationToken)
    {
        role = role is "assistant" ? "assistant" : "user";
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 32_000) text = text[..32_000];
        await using var scope = scopes.CreateOwnerScope(ownerId);
        await PersistAsync(scope.ServiceProvider, ownerId, conversationId, role, text, cancellationToken);
    }

    private static async Task<AIFunction?> ResolveFunctionAsync(IServiceProvider services, Guid ownerId,
        Guid conversationId, string toolName, CancellationToken cancellationToken)
    {
        var mcp = services.GetRequiredService<McpToolHost>();
        await mcp.InitializeAsync(cancellationToken);
        var functions = services.GetRequiredService<JarvisAgentFactory>()
            .CollectFunctions(mcp.Tools, new AgentBuildContext(ownerId, null, conversationId));
        return functions.FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
    }

    private async Task<VoiceToolInvocation> ExecuteAsync(IServiceProvider services, Guid ownerId, Guid conversationId,
        AIFunction function, string? argumentsJson, CancellationToken cancellationToken)
    {
        var callId = Guid.CreateVersion7().ToString("N");
        await PublishAsync(conversationId, "tool.started", new { conversationId, tool = function.Name },
            cancellationToken);
        try
        {
            var result = VoiceTools.FormatResult(await function.InvokeAsync(
                VoiceTools.ToArguments(argumentsJson), cancellationToken));
            await PublishAsync(conversationId, "tool.completed", new { conversationId, tool = function.Name },
                cancellationToken);
            return new VoiceToolInvocation(result, false, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Voice tool {ToolName} failed for conversation {ConversationId}.",
                function.Name, conversationId);
            await PublishAsync(conversationId, "tool.failed", new { conversationId, tool = function.Name },
                cancellationToken);
            return new VoiceToolInvocation("That Jarvis tool could not complete. Check the Jarvis app.", true, false);
        }
        finally
        {
            _ = callId;
            _ = ownerId;
        }
    }

    private async Task<ToolApprovalRecord> CreateApprovalAsync(IServiceProvider services, Guid ownerId,
        Guid conversationId, string toolName, string argumentsJson, CancellationToken cancellationToken)
    {
        var created = await services.GetRequiredService<IToolApprovalStore>().CreateAsync(ownerId, conversationId,
            VoiceTools.NewApprovalRequestId(), Guid.CreateVersion7().ToString("N"), toolName, argumentsJson, null,
            cancellationToken);
        var approval = created.Approval;
        await PublishAsync(conversationId, "tool.approval_required", new
        {
            approval.Id,
            approval.ConversationId,
            approval.ToolName,
            approval.ArgumentsJson,
            approval.CreatedAt
        }, cancellationToken);
        if (created.Created)
            await PublishToOwnerAsync(ownerId, "notification.created", new
            {
                type = "approval.required",
                notificationId = created.NotificationId,
                title = "Approval needed",
                body = $"Jarvis is waiting for approval to run {approval.ToolName}.",
                sourceId = approval.Id
            }, conversationId, cancellationToken);
        await PublishAsync(conversationId, "agent.waiting_for_approval", new { conversationId }, cancellationToken);
        return approval;
    }

    private async Task PersistAsync(IServiceProvider services, Guid ownerId, Guid conversationId, string role,
        string text, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IConversationStore>();
        var existing = await store.GetMessagesAsync(conversationId, cancellationToken);
        var last = existing.Count > 0 ? existing[^1] : null;
        if (last is not null && last.Role == role && last.Content == text) return;
        var message = new Message(conversationId, role, text);
        await store.AddMessageAsync(message, cancellationToken);
        if (role == "user")
            await PublishAsync(conversationId, "voice.transcript", new { conversationId, text }, cancellationToken);
        await PublishAsync(conversationId, "message.completed", new
        {
            id = message.Id,
            role = message.Role,
            content = message.Content,
            createdAt = message.CreatedAt
        }, cancellationToken);
        if (role == "assistant" && last is { Role: "user" })
            QueueMemoryExtraction(ownerId, conversationId, last.Id, last.Content);
    }

    private void QueueMemoryExtraction(Guid ownerId, Guid conversationId, Guid sourceMessageId, string source)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await scope.ServiceProvider.GetRequiredService<IConversationMemoryExtractor>()
                    .ExtractAndStoreAsync(ownerId, sourceMessageId, source, timeout.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Memory extraction failed after a voice turn for conversation {ConversationId}.", conversationId);
            }
        });
    }

    private async Task PublishAsync(Guid conversationId, string eventName, object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(JarvisEventsHub.GroupName(conversationId))
                .SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not publish {EventName} for conversation {ConversationId}.",
                eventName, conversationId);
        }
    }

    private async Task PublishToOwnerAsync(Guid ownerId, string eventName, object payload, Guid conversationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(JarvisEventsHub.OwnerGroupName(ownerId))
                .SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not publish {EventName} for conversation {ConversationId}.",
                eventName, conversationId);
        }
    }
}

public sealed record VoiceSessionSnapshot(string Instructions, IReadOnlyList<VoiceToolDescriptor> Tools);

public sealed record VoiceToolInvocation(string Result, bool IsError, bool ApprovalRequired, Guid? ApprovalId = null);
