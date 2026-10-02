using Jarvis.Application.Conversations;
using Jarvis.Application.Projects;

namespace Jarvis.Api.Endpoints;

public sealed record ProjectConversationDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    bool Pinned);
public sealed record ProjectDetailsDto(ProjectRecord Project, IReadOnlyList<ProjectConversationDto> Conversations,
    IReadOnlyList<FileDto> Files, IReadOnlyList<JarvisTaskDto> Tasks);

/// <summary>Moves an item into a project, or out of its project when <see cref="ProjectId"/> is null.</summary>
public sealed record AssignProjectRequest(Guid? ProjectId);

internal static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects", async (IProjectStore projects, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await projects.ListAsync(currentUser.OwnerId, ct)))
            .WithName("ListProjects");

        api.MapPost("/projects", async (ProjectInput request, IProjectStore projects, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                var project = await projects.CreateAsync(currentUser.OwnerId, request, ct);
                return Results.Created($"/api/v1/projects/{project.Id}", project);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid(exception.ParamName ?? "name", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }
        }).WithName("CreateProject");

        api.MapGet("/projects/{projectId:guid}", async (Guid projectId, IProjectStore projects,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var project = await projects.GetAsync(projectId, currentUser.OwnerId, ct);
            var contents = project is null ? null : await projects.GetContentsAsync(projectId, currentUser.OwnerId, ct);
            if (project is null || contents is null) return Results.NotFound();
            return Results.Ok(new ProjectDetailsDto(project,
                contents.Conversations.Select(item => new ProjectConversationDto(item.Id, item.Title, item.CreatedAt,
                    item.UpdatedAt, item.PinnedAt is not null)).ToArray(),
                contents.Files.Select(file => file.ToDto()).ToArray(),
                contents.Tasks.Select(task => task.ToDto()).ToArray()));
        }).WithName("GetProject");

        api.MapPut("/projects/{projectId:guid}", async (Guid projectId, ProjectInput request, IProjectStore projects,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var project = await projects.UpdateAsync(projectId, currentUser.OwnerId, request, ct);
                return project is null ? Results.NotFound() : Results.Ok(project);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid(exception.ParamName ?? "name", exception.Message);
            }
        }).WithName("UpdateProject");

        api.MapDelete("/projects/{projectId:guid}", async (Guid projectId, IProjectStore projects,
                ICurrentUser currentUser, CancellationToken ct) =>
                await projects.DeleteAsync(projectId, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteProject");

        api.MapPut("/conversations/{conversationId:guid}/project", async (Guid conversationId,
                AssignProjectRequest request, IProjectStore projects, ICurrentUser currentUser, CancellationToken ct) =>
                ToResult(await projects.AssignConversationAsync(conversationId, request.ProjectId, currentUser.OwnerId,
                    ct)))
            .WithName("MoveConversationToProject");

        api.MapPut("/files/{fileId:guid}/project", async (Guid fileId, AssignProjectRequest request,
                IProjectStore projects, ICurrentUser currentUser, CancellationToken ct) =>
                ToResult(await projects.AssignFileAsync(fileId, request.ProjectId, currentUser.OwnerId, ct)))
            .WithName("MoveFileToProject");

        api.MapPut("/tasks/{taskId:guid}/project", async (Guid taskId, AssignProjectRequest request,
                IProjectStore projects, ICurrentUser currentUser, CancellationToken ct) =>
                ToResult(await projects.AssignTaskAsync(taskId, request.ProjectId, currentUser.OwnerId, ct)))
            .WithName("MoveTaskToProject");

        return api;
    }

    private static IResult ToResult(ProjectAssignResult result) => result switch
    {
        ProjectAssignResult.Assigned => Results.NoContent(),
        ProjectAssignResult.ProjectNotFound => EndpointHelpers.Invalid("projectId", "Project was not found."),
        _ => Results.NotFound()
    };
}
