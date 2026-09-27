using Jarvis.Api.Realtime;
using Jarvis.Application.Audit;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Endpoints;

internal static class EndpointHelpers
{
    public static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static async Task TryAppendAuditAsync(IAuditEventStore audit, ILogger logger, Guid ownerId, string tool,
        string action, string riskClass, bool success, Guid? approvalId, string? metadataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            await audit.AppendAsync(ownerId, tool, action, riskClass, success, approvalId, metadataJson,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not append audit event {Action}.", action);
        }
    }

    public static async Task PublishAgentFailedAsync(IHubContext<JarvisEventsHub> hub, ILogger logger,
        Guid conversationId, string message)
    {
        try
        {
            await hub.Clients.Group(JarvisEventsHub.GroupName(conversationId))
                .SendAsync("agent.failed", new { conversationId, message }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish agent.failed for conversation {ConversationId}.",
                conversationId);
        }
    }
}
