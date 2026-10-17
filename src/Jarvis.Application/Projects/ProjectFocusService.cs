using Jarvis.Application.Decisions;
using Jarvis.Application.Events;
using Jarvis.Application.Inbox;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Projects;

public sealed class ProjectFocusService(IProjectStore projects, ICommitmentService commitments,
    IDecisionService decisions, IReminderService reminders, IJarvisTaskService tasks) : IProjectFocusService
{
    /// <summary>What a project's next action can point at. A personal to-do is a commitment; an agent task stays a task.</summary>
    public static readonly IReadOnlySet<string> LinkableTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        EntityTypes.Commitment, EntityTypes.Decision, EntityTypes.Reminder, EntityTypes.Task
    };

    public async Task<ProjectRecord?> SetAsync(Guid ownerId, Guid projectId, ProjectFocusInput input,
        CancellationToken cancellationToken)
    {
        var link = input.NextAction;
        if (link is { Type: not null } or { Id: not null })
        {
            if (link.Type is null || link.Id is null)
                throw new ArgumentException("A linked next action needs both a type and an id.", "nextAction");
            var type = link.Type.Trim().ToLowerInvariant();
            if (!LinkableTypes.Contains(type))
                throw new ArgumentException("Link a commitment, decision, reminder or task.", "nextAction");
            var view = await ResolveAsync(ownerId, type, link.Id.Value, cancellationToken);
            if (view.State == ProjectNextActionStates.Gone)
                throw new ArgumentException("That item was not found.", "nextAction");
            input = input with { NextAction = link with { Type = type } };
        }

        return await projects.SetFocusAsync(projectId, ownerId, input, cancellationToken);
    }

    public async Task<ProjectNextActionView?> DescribeAsync(Guid ownerId, ProjectRecord project,
        CancellationToken cancellationToken)
    {
        if (project.NextAction is not { } next) return null;
        if (next.Type is null || next.Id is null)
            return new ProjectNextActionView(null, null, next.Note, null, ProjectNextActionStates.Open);
        var resolved = await ResolveAsync(ownerId, next.Type, next.Id.Value, cancellationToken);
        return resolved with { Note = next.Note };
    }

    private async Task<ProjectNextActionView> ResolveAsync(Guid ownerId, string type, Guid id,
        CancellationToken cancellationToken)
    {
        string? title = null;
        var state = ProjectNextActionStates.Gone;
        switch (type)
        {
            case EntityTypes.Commitment:
                if (await commitments.GetAsync(id, ownerId, cancellationToken) is { } commitment)
                {
                    title = commitment.Description;
                    state = commitment.Status == CommitmentStatuses.Open
                        ? ProjectNextActionStates.Open
                        : ProjectNextActionStates.Done;
                }
                break;
            case EntityTypes.Decision:
                if (await decisions.GetAsync(id, ownerId, cancellationToken) is { } decision)
                {
                    title = decision.Title;
                    state = decision.Status == DecisionStatuses.Resolved
                        ? ProjectNextActionStates.Done
                        : ProjectNextActionStates.Open;
                }
                break;
            case EntityTypes.Reminder:
                if (await reminders.GetAsync(id, ownerId, cancellationToken) is { } reminder)
                {
                    title = reminder.Title;
                    state = reminder.Status is "done" or "completed" or "cancelled"
                        ? ProjectNextActionStates.Done
                        : ProjectNextActionStates.Open;
                }
                break;
            case EntityTypes.Task:
                if ((await tasks.ListAsync(ownerId, cancellationToken)).FirstOrDefault(task => task.Id == id) is { } task)
                {
                    title = task.Title;
                    state = task.Status is "completed" or "failed" or "cancelled"
                        ? ProjectNextActionStates.Done
                        : ProjectNextActionStates.Open;
                }
                break;
        }

        return new ProjectNextActionView(type, id, null, title, state);
    }
}
