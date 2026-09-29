using Jarvis.Api.Endpoints;
using Jarvis.Api.Errors;

namespace Jarvis.Api.Conversations;

internal static class RemoteQueryResults
{
    public static async Task<IResult> ExecuteAsync(Func<Task<ConversationTurnResult>> run)
    {
        try
        {
            return (await run()).ToHttpResult();
        }
        catch (OperationCanceledException)
        {
            var httpContext = ApiProblemResults.CurrentContext;
            if (httpContext is null)
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            return ApiProblemResults.RequestCancelled(httpContext);
        }
    }
}

internal static class ConversationTurnResults
{
    public static IResult ToHttpResult(this ConversationTurnResult result) => result switch
    {
        ConversationTurnResult.NotFound => ApiProblemResults.NotFound(),
        ConversationTurnResult.Conflict conflict => ApiProblemResults.Conflict(conflict.Message),
        ConversationTurnResult.AwaitingApproval pending => Results.Accepted("/api/v1/approvals",
            pending.Approvals.ToDtos()),
        ConversationTurnResult.Completed completed => Results.Ok(completed.Message.ToDto()),
        ConversationTurnResult.Failed failed => ApiProblemResults.Failed(failed.Message),
        _ => throw new InvalidOperationException("Unknown conversation turn result.")
    };
}
