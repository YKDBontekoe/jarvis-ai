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
            IJarvisTaskRepository tasks, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
            if (conversation is null) return Results.NotFound();
            if (await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
                return Results.NotFound();
            var messages = await store.GetMessagesAsync(conversationId, ct);
            return Results.Ok(new ConversationDetailsDto(conversation.Id, conversation.Title, conversation.CreatedAt,
                conversation.UpdatedAt, messages.Select(message => message.ToDto()).ToArray()));
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
            SendMessageRequest request, ConversationTurnService turns, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var content = request.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 32_000)
                return EndpointHelpers.Invalid("content", "Message content must contain 1 to 32,000 characters.");
            return (await turns.SendAsync(currentUser.OwnerId, conversationId, content, ct)).ToHttpResult();
        }).WithName("SendMessage");

        return api;
    }

    public static RouteGroupBuilder MapApprovalEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/approvals", async (IToolApprovalStore approvals, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await approvals.ListActionableAsync(currentUser.OwnerId, ct)).ToDtos()))
            .WithName("ListPendingApprovals");

        api.MapPost("/approvals/{approvalId:guid}/decision", async (Guid approvalId, ApprovalDecisionRequest request,
                ApprovalDecisionService decisions, ICurrentUser currentUser, CancellationToken ct) =>
            (await decisions.DecideAsync(currentUser.OwnerId, approvalId, request.Approved, ct)).ToHttpResult())
            .WithName("DecideToolApproval");

        return api;
    }
}
