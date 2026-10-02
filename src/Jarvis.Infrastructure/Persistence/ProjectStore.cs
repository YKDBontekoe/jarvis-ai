using System.Text.Json;
using Jarvis.Application.Projects;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

/// <summary>Owner-scoped projects. A task belongs to a project through its own conversation.</summary>
public sealed class ProjectStore(JarvisDbContext db) : IProjectStore
{
    internal const int MaxContextFiles = 25;

    public async Task<IReadOnlyList<ProjectRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var projects = await db.Projects.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken);
        if (projects.Count == 0) return [];
        var counts = await CountsAsync(ownerId, projects.Select(x => x.Id).ToArray(), cancellationToken);
        return projects.Select(project => ToRecord(project, counts)).ToArray();
    }

    public async Task<ProjectRecord?> GetAsync(Guid projectId, Guid ownerId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == projectId && x.OwnerId == ownerId, cancellationToken);
        if (project is null) return null;
        return ToRecord(project, await CountsAsync(ownerId, [project.Id], cancellationToken));
    }

    public async Task<ProjectRecord> CreateAsync(Guid ownerId, ProjectInput input, CancellationToken cancellationToken)
    {
        var project = new Project(ownerId, input.Name ?? string.Empty, input.Description, input.Instructions,
            input.Color);
        if (await db.Projects.CountAsync(x => x.OwnerId == ownerId, cancellationToken) >= Project.MaxProjectsPerOwner)
            throw new InvalidOperationException($"You can have up to {Project.MaxProjectsPerOwner} projects.");
        db.Projects.Add(project);
        AddAudit(ownerId, "project.created", project.Id);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(project, Counts.Empty);
    }

    public async Task<ProjectRecord?> UpdateAsync(Guid projectId, Guid ownerId, ProjectInput input,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .SingleOrDefaultAsync(x => x.Id == projectId && x.OwnerId == ownerId, cancellationToken);
        if (project is null) return null;
        project.Update(input.Name ?? string.Empty, input.Description, input.Instructions, input.Color);
        AddAudit(ownerId, "project.updated", project.Id);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(project, await CountsAsync(ownerId, [project.Id], cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var project = await db.Projects
            .SingleOrDefaultAsync(x => x.Id == projectId && x.OwnerId == ownerId, cancellationToken);
        if (project is null) return false;
        // The foreign keys also set these to null, but clearing them here keeps tracked entities consistent.
        await db.Conversations.Where(x => x.OwnerId == ownerId && x.ProjectId == projectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectId, (Guid?)null), cancellationToken);
        await db.Files.Where(x => x.OwnerId == ownerId && x.ProjectId == projectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectId, (Guid?)null), cancellationToken);
        db.Projects.Remove(project);
        AddAudit(ownerId, "project.deleted", projectId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ProjectContents?> GetContentsAsync(Guid projectId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await OwnsProjectAsync(projectId, ownerId, cancellationToken)) return null;

        var conversations = await db.Conversations.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ProjectId == projectId)
            .Where(x => !db.Tasks.Any(task => task.ConversationId == x.Id))
            .OrderByDescending(x => x.PinnedAt != null).ThenByDescending(x => x.PinnedAt)
            .ThenByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        var files = await db.Files.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(task => task.OwnerId == ownerId && db.Conversations.Any(conversation =>
                conversation.Id == task.ConversationId && conversation.ProjectId == projectId))
            .OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);

        return new ProjectContents(conversations, files.Select(x => x.ToRecord()).ToArray(),
            tasks.Select(x => x.ToRecord()).ToArray());
    }

    public async Task<ProjectAssignResult> AssignConversationAsync(Guid conversationId, Guid? projectId,
        Guid ownerId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations
            .SingleOrDefaultAsync(x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);
        if (conversation is null) return ProjectAssignResult.ItemNotFound;
        if (!await TouchTargetAsync(projectId, ownerId, cancellationToken)) return ProjectAssignResult.ProjectNotFound;
        conversation.MoveToProject(projectId);
        await db.SaveChangesAsync(cancellationToken);
        return ProjectAssignResult.Assigned;
    }

    public async Task<ProjectAssignResult> AssignFileAsync(Guid fileId, Guid? projectId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var file = await db.Files.SingleOrDefaultAsync(x => x.Id == fileId && x.OwnerId == ownerId, cancellationToken);
        if (file is null) return ProjectAssignResult.ItemNotFound;
        if (!await TouchTargetAsync(projectId, ownerId, cancellationToken)) return ProjectAssignResult.ProjectNotFound;
        file.ProjectId = projectId;
        await db.SaveChangesAsync(cancellationToken);
        return ProjectAssignResult.Assigned;
    }

    public async Task<ProjectAssignResult> AssignTaskAsync(Guid taskId, Guid? projectId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var conversationId = await db.Tasks.AsNoTracking().Where(x => x.Id == taskId && x.OwnerId == ownerId)
            .Select(x => (Guid?)x.ConversationId).SingleOrDefaultAsync(cancellationToken);
        return conversationId is { } id
            ? await AssignConversationAsync(id, projectId, ownerId, cancellationToken)
            : ProjectAssignResult.ItemNotFound;
    }

    public async Task<ProjectContext?> GetContextForConversationAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var projectId = await db.Conversations.AsNoTracking()
            .Where(x => x.Id == conversationId && x.OwnerId == ownerId)
            .Select(x => x.ProjectId).SingleOrDefaultAsync(cancellationToken);
        if (projectId is null) return null;
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == projectId && x.OwnerId == ownerId, cancellationToken);
        if (project is null) return null;

        var files = db.Files.AsNoTracking().Where(x => x.OwnerId == ownerId && x.ProjectId == project.Id);
        var total = await files.CountAsync(cancellationToken);
        var listed = await files.OrderByDescending(x => x.CreatedAt).Take(MaxContextFiles)
            .Select(x => new ProjectFileReference(x.Id, x.FileName, x.ProcessingStatus))
            .ToListAsync(cancellationToken);
        return new ProjectContext(project.Id, project.Name, project.Description, project.Instructions, listed, total);
    }

    /// <summary>True when there is no target or the owner's target project exists; marks it as used.</summary>
    private async Task<bool> TouchTargetAsync(Guid? projectId, Guid ownerId, CancellationToken cancellationToken)
    {
        if (projectId is not { } id) return true;
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        project?.Touch();
        return project is not null;
    }

    private Task<bool> OwnsProjectAsync(Guid projectId, Guid ownerId, CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.OwnerId == ownerId, cancellationToken);

    private async Task<Counts> CountsAsync(Guid ownerId, Guid[] projectIds, CancellationToken cancellationToken)
    {
        var inProjects = db.Conversations.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ProjectId != null && projectIds.Contains(x.ProjectId.Value));
        var conversations = await inProjects
            .Where(x => !db.Tasks.Any(task => task.ConversationId == x.Id))
            .GroupBy(x => x.ProjectId!.Value).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var tasks = await inProjects
            .Where(x => db.Tasks.Any(task => task.ConversationId == x.Id))
            .GroupBy(x => x.ProjectId!.Value).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var files = await db.Files.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.ProjectId != null && projectIds.Contains(x.ProjectId.Value))
            .GroupBy(x => x.ProjectId!.Value).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return new Counts(conversations, files, tasks);
    }

    private static ProjectRecord ToRecord(Project project, Counts counts) =>
        new(project.Id, project.Name, project.Description, project.Instructions, project.Color, project.CreatedAt,
            project.UpdatedAt, counts.Conversations.GetValueOrDefault(project.Id),
            counts.Files.GetValueOrDefault(project.Id), counts.Tasks.GetValueOrDefault(project.Id));

    private void AddAudit(Guid ownerId, string action, Guid projectId) =>
        db.AuditEvents.Add(new AuditEvent(ownerId, "projects", action, "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = projectId })));

    private sealed record Counts(IReadOnlyDictionary<Guid, int> Conversations, IReadOnlyDictionary<Guid, int> Files,
        IReadOnlyDictionary<Guid, int> Tasks)
    {
        public static readonly Counts Empty = new(new Dictionary<Guid, int>(), new Dictionary<Guid, int>(),
            new Dictionary<Guid, int>());
    }
}
