using Jarvis.Api.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Endpoints;

internal static class ConversationEndpoints
{
    public static RouteGroupBuilder MapConversationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/conversations", async (IConversationStore store, ICurrentUser currentUser,
            CreateConversationRequest request, CancellationToken ct) =>
        {
            var title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title.Trim();
            if (title.Length > 200) return EndpointHelpers.Invalid("title", "Title must be 200 characters or fewer.");
            var conversation = await store.CreateAsync(currentUser.OwnerId, title, ct);
            return Results.Created($"/api/v1/conversations/{conversation.Id}",
                new ConversationDto(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt));
        }).WithName("CreateConversation");

        api.MapGet("/conversations", async (IConversationStore store, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await store.ListAsync(currentUser.OwnerId, ct))
                    .Select(x => new ConversationDto(x.Id, x.Title, x.CreatedAt, x.UpdatedAt))))
            .WithName("ListConversations");

        api.MapGet("/conversations/{conversationId:guid}", async (Guid conversationId, IConversationStore store,
            IJarvisTaskRepository tasks, ICurrentUser currentUser, RemoteQueryHost queries, CancellationToken ct) =>
        {
            var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
            if (conversation is null) return Results.NotFound();
            if (await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
                return Results.NotFound();
            var messages = await store.GetMessagesAsync(conversationId, ct);
            return Results.Ok(new ConversationDetailsDto(conversation.Id, conversation.Title, conversation.CreatedAt,
                conversation.UpdatedAt, messages.Select(message => message.ToDto()).ToArray(),
                queries.IsResponding(conversationId)));
        }).WithName("GetConversation");

        api.MapDelete("/conversations/{conversationId:guid}", async (Guid conversationId,
            IConversationStore store, IConversationRunLock runLock, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await using var lease = await runLock.AcquireAsync(conversationId, ct);
            var result = await store.DeleteAsync(conversationId, currentUser.OwnerId, ct);
            if (result == ConversationDeleteResult.NotFound) return Results.NotFound();
            if (result == ConversationDeleteResult.TaskBacked)
                return Results.Conflict(new { message = "Task conversations are managed from the Tasks section." });
            return Results.NoContent();
        }).WithName("DeleteConversation");

        api.MapPost("/conversations/{conversationId:guid}/messages", async (Guid conversationId,
            SendMessageRequest request, RemoteQueryExecutor remote, ICurrentUser currentUser) =>
        {
            var content = request.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 32_000)
                return EndpointHelpers.Invalid("content", "Message content must contain 1 to 32,000 characters.");
            // The request abort token is intentionally unused. Closing the phone drops this connection,
            // and the query still runs until it is stored.
            return await RemoteQueryResults.ExecuteAsync(() =>
                remote.SendAsync(currentUser.OwnerId, conversationId, content));
        }).WithName("SendMessage");

        api.MapPost("/conversations/{conversationId:guid}/cancel", async (Guid conversationId,
            IConversationStore store, ICurrentUser currentUser, RemoteQueryHost queries, CancellationToken ct) =>
        {
            if (await store.GetAsync(conversationId, currentUser.OwnerId, ct) is null)
                return Results.NotFound();
            queries.TryCancel(currentUser.OwnerId, conversationId);
            return Results.NoContent();
        }).WithName("CancelConversationRun");

        return api;
    }

    public static RouteGroupBuilder MapApprovalEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/approvals", async (IToolApprovalStore approvals, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await approvals.ListActionableAsync(currentUser.OwnerId, ct)).ToDtos()))
            .WithName("ListPendingApprovals");

        api.MapPost("/approvals/{approvalId:guid}/decision", async (Guid approvalId, ApprovalDecisionRequest request,
                RemoteQueryExecutor remote, ICurrentUser currentUser) =>
            await RemoteQueryResults.ExecuteAsync(() =>
                remote.DecideAsync(currentUser.OwnerId, approvalId, request.Approved)))
            .WithName("DecideToolApproval");

        return api;
    }
}
