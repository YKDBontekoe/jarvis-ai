using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// Workspaces: a saved profile and project for a recurring job. Launching only tells the app what to start; the chat is
/// made with the usual endpoint, so the profile's own snapshot and confirmation rules apply.
/// </summary>
internal static class WorkspaceEndpoints
{
    public static RouteGroupBuilder MapWorkspaceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/workspaces").WithTags("Workspaces");

        group.MapGet("", async (IWorkspaceService workspaces, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await workspaces.ListAsync(user.OwnerId, ct))).WithName("ListWorkspaces");

        group.MapPost("", async (WorkspaceInput input, IWorkspaceService workspaces, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await workspaces.SaveAsync(user.OwnerId, null, input, ct);
            return result.Succeeded ? Results.Created($"/api/v1/workspaces/{result.Value!.Workspace.Id}", result.Value) : Respond(result);
        }).WithName("CreateWorkspace");

        group.MapPut("/{id:guid}", async (Guid id, WorkspaceInput input, IWorkspaceService workspaces, ICurrentUser user,
            CancellationToken ct) => Respond(await workspaces.SaveAsync(user.OwnerId, id, input, ct))).WithName("UpdateWorkspace");

        group.MapDelete("/{id:guid}", async (Guid id, IWorkspaceService workspaces, ICurrentUser user, CancellationToken ct) =>
            await workspaces.DeleteAsync(user.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound()).WithName("DeleteWorkspace");

        group.MapPost("/{id:guid}/launch", async (Guid id, IWorkspaceService workspaces, ICurrentUser user, CancellationToken ct) =>
            Respond(await workspaces.LaunchAsync(user.OwnerId, id, ct))).WithName("LaunchWorkspace");

        return api;
    }

    private static IResult Respond<T>(WorkspaceOperation<T> result) => result switch
    {
        { NotFound: true } => Results.NotFound(),
        { Succeeded: false } => EndpointHelpers.Invalid(result.Field ?? "request", result.Message ?? "The request is invalid."),
        _ => Results.Ok(result.Value)
    };
}
