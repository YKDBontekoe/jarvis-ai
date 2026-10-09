using Jarvis.Application.Browser;
using Jarvis.Application.Conversations;

namespace Jarvis.Api.Endpoints;

public sealed record BrowserSessionDto(Guid Id, Guid ConversationId, string Goal, string? StartUrl, string Status,
    DateTimeOffset CreatedAt, IReadOnlyList<BrowserStepDto> Steps, string Kind, string ControlMode);
public sealed record BrowserStepDto(int Ordinal, string Tool, string Summary, bool Success, DateTimeOffset CreatedAt,
    bool HasScreenshot);

internal static class BrowserEndpoints
{
    public static RouteGroupBuilder MapBrowserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/conversations/{conversationId:guid}/browser-sessions", async (Guid conversationId,
            IBrowserSessionStore sessions, IConversationStore conversations, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (await conversations.GetAsync(conversationId, currentUser.OwnerId, ct) is null)
                return Results.NotFound();
            var list = await sessions.ListForConversationAsync(currentUser.OwnerId, conversationId, ct);
            return Results.Ok(list.Select(ToDto));
        }).WithName("ListBrowserSessions");

        api.MapGet("/browser-sessions/{id:guid}", async (Guid id, IBrowserSessionStore sessions,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(currentUser.OwnerId, id, ct);
            return session is null ? Results.NotFound() : Results.Ok(ToDto(session));
        }).WithName("GetBrowserSession");

        return api;
    }

    internal static BrowserSessionDto ToDto(BrowserSessionRecord session) =>
        new(session.Id, session.ConversationId, session.Goal, session.StartUrl, session.Status, session.CreatedAt,
            session.Steps.Select(step => new BrowserStepDto(step.Ordinal, step.Tool, step.Summary, step.Success,
                step.CreatedAt, step.ScreenshotKey is not null)).ToArray(), session.Kind, session.ControlMode);
}
