using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Files;

namespace Jarvis.Application.Projects;

/// <summary>A project with how much it holds, for lists and the project header.</summary>
public sealed record ProjectRecord(Guid Id, string Name, string? Description, string? Instructions, string Color,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int ConversationCount, int FileCount, int TaskCount);

public sealed record ProjectInput(string? Name, string? Description, string? Instructions, string? Color);

/// <summary>What Jarvis is told about the project a conversation belongs to.</summary>
public sealed record ProjectContext(Guid Id, string Name, string? Description, string? Instructions,
    IReadOnlyList<ProjectFileReference> Files, int TotalFiles);

public sealed record ProjectFileReference(Guid Id, string FileName, string ProcessingStatus);

/// <summary>Everything in a project, newest first.</summary>
public sealed record ProjectContents(IReadOnlyList<Conversation> Conversations, IReadOnlyList<StoredFile> Files,
    IReadOnlyList<JarvisTaskRecord> Tasks);

public enum ProjectAssignResult
{
    Assigned,
    ItemNotFound,
    ProjectNotFound
}

public interface IProjectStore
{
    Task<IReadOnlyList<ProjectRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ProjectRecord?> GetAsync(Guid projectId, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Creates a project. Throws <see cref="ArgumentException"/> for invalid input and
    /// <see cref="InvalidOperationException"/> when the owner has reached the project limit.</summary>
    Task<ProjectRecord> CreateAsync(Guid ownerId, ProjectInput input, CancellationToken cancellationToken);
    Task<ProjectRecord?> UpdateAsync(Guid projectId, Guid ownerId, ProjectInput input,
        CancellationToken cancellationToken);

    /// <summary>Deletes the project only. Its conversations, files, and tasks stay and leave the project.</summary>
    Task<bool> DeleteAsync(Guid projectId, Guid ownerId, CancellationToken cancellationToken);

    Task<ProjectContents?> GetContentsAsync(Guid projectId, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Moves a conversation (a chat or a task's conversation) into a project, or out with null.</summary>
    Task<ProjectAssignResult> AssignConversationAsync(Guid conversationId, Guid? projectId, Guid ownerId,
        CancellationToken cancellationToken);
    Task<ProjectAssignResult> AssignFileAsync(Guid fileId, Guid? projectId, Guid ownerId,
        CancellationToken cancellationToken);
    Task<ProjectAssignResult> AssignTaskAsync(Guid taskId, Guid? projectId, Guid ownerId,
        CancellationToken cancellationToken);

    Task<ProjectContext?> GetContextForConversationAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken);
}
