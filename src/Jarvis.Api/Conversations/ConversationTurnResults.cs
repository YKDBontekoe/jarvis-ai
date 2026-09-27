using Jarvis.Api.Endpoints;

namespace Jarvis.Api.Conversations;

internal static class ConversationTurnResults
{
    public static IResult ToHttpResult(this ConversationTurnResult result) => result switch
    {
        ConversationTurnResult.NotFound => Results.NotFound(),
        ConversationTurnResult.Conflict conflict => Results.Conflict(new { message = conflict.Message }),
        ConversationTurnResult.AwaitingApproval pending => Results.Accepted("/api/v1/approvals",
            pending.Approvals.ToDtos()),
        ConversationTurnResult.Completed completed => Results.Ok(completed.Message.ToDto()),
        ConversationTurnResult.Failed failed => Results.Problem(failed.Message,
            statusCode: StatusCodes.Status502BadGateway),
        _ => throw new InvalidOperationException("Unknown conversation turn result.")
    };
}
