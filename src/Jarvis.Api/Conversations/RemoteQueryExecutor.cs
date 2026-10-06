using Jarvis.Api.Security;

namespace Jarvis.Api.Conversations;

/// <summary>
/// Runs a conversation turn in its own owner scope so a closed phone cannot cancel it.
/// The HTTP request may still wait for the result while the client is connected.
/// </summary>
public sealed class RemoteQueryExecutor(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    RemoteQueryHost queries)
{
    public async Task<ConversationTurnResult> SendAsync(Guid ownerId, Guid conversationId, string content,
        Func<string, CancellationToken, Task>? onTextDelta = null,
        Func<CancellationToken, Task>? beforeRun = null,
        IReadOnlyList<Jarvis.Application.Conversations.MessageAttachment>? attachments = null)
    {
        using var query = queries.Begin(ownerId, conversationId, lifetime.ApplicationStopping);
        await using var scope = scopes.CreateOwnerScope(ownerId);
        var turns = scope.ServiceProvider.GetRequiredService<ConversationTurnService>();
        try
        {
            return await turns.SendAsync(ownerId, conversationId, content, query.Token, onTextDelta, beforeRun,
                attachments);
        }
        catch (OperationCanceledException) when (query.StoppedByOwner)
        {
            throw new RunStoppedException();
        }
    }

    public async Task<ConversationTurnResult> RegenerateAsync(Guid ownerId, Guid conversationId)
    {
        using var query = queries.Begin(ownerId, conversationId, lifetime.ApplicationStopping);
        await using var scope = scopes.CreateOwnerScope(ownerId);
        var turns = scope.ServiceProvider.GetRequiredService<ConversationTurnService>();
        try
        {
            return await turns.RegenerateAsync(ownerId, conversationId, query.Token);
        }
        catch (OperationCanceledException) when (query.StoppedByOwner)
        {
            throw new RunStoppedException();
        }
    }

    public async Task<ConversationTurnResult> DecideAsync(Guid ownerId, Guid approvalId, bool approved,
        bool rememberCategory = false, Jarvis.Application.Approvals.GrantTerms? terms = null)
    {
        await using var scope = scopes.CreateOwnerScope(ownerId);
        var approvals = scope.ServiceProvider.GetRequiredService<Jarvis.Application.Approvals.IToolApprovalStore>();
        var pending = await approvals.GetActionableAsync(approvalId, ownerId, CancellationToken.None);
        if (pending is null) return new ConversationTurnResult.NotFound();

        using var query = queries.Begin(ownerId, pending.ConversationId, lifetime.ApplicationStopping);
        var decisions = scope.ServiceProvider.GetRequiredService<ApprovalDecisionService>();
        try
        {
            return await decisions.DecideAsync(ownerId, approvalId, approved, query.Token, rememberCategory, terms);
        }
        catch (OperationCanceledException) when (query.StoppedByOwner)
        {
            throw new RunStoppedException();
        }
    }
}
