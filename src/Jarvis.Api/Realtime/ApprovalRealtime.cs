using Jarvis.Application.Approvals;
using Jarvis.Domain.Approvals;

namespace Jarvis.Api.Realtime;

internal static class ApprovalRealtime
{
    public static object Required(ToolApprovalRecord approval) =>
        Payload(approval.Id, approval.ConversationId, approval.ToolName, approval.ArgumentsJson, approval.CreatedAt);

    public static object Required(ToolApproval approval) =>
        Payload(approval.Id, approval.ConversationId, approval.ToolName, approval.ArgumentsJson, approval.CreatedAt);

    private static object Payload(Guid id, Guid conversationId, string toolName, string argumentsJson,
        DateTimeOffset createdAt)
    {
        var category = ApprovalCategories.Resolve(toolName, argumentsJson);
        return new
        {
            id,
            conversationId,
            toolName,
            argumentsJson,
            createdAt,
            category = category.Key,
            categoryLabel = category.Label,
            canRememberCategory = category.CanRemember
        };
    }
}
