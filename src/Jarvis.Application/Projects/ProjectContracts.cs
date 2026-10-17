using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Files;

namespace Jarvis.Application.Projects;

/// <summary>A project with how much it holds, for lists and the project header.</summary>
public sealed record ProjectRecord(Guid Id, string Name, string? Description, string? Instructions, string Color,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int ConversationCount, int FileCount, int TaskCount,
    string? Outcome = null, string Status = "active", ProjectNextAction? NextAction = null);

/// <summary>The owner's chosen next step. <see cref="Type"/> and <see cref="Id"/> are both set or both null.</summary>
public sealed record ProjectNextAction(string? Type, Guid? Id, string? Note);

/// <summary>
/// Replaces a project's outcome and next action. A null <see cref="Status"/> keeps the status; a null
/// <see cref="NextAction"/> clears the next action.
/// </summary>
public sealed record ProjectFocusInput(string? Outcome, string? Status, ProjectNextAction? NextAction);

/// <summary>A project's next action as the owner sees it: the linked thing's current title and state.</summary>
public sealed record ProjectNextActionView(string? Type, Guid? Id, string? Note, string? Title, string State);

public static class ProjectNextActionStates
{
    /// <summary>Only a note, or a linked thing that is still to do.</summary>
    public const string Open = "open";

    /// <summary>The linked thing is finished; the owner should choose another next step.</summary>
    public const string Done = "done";

    /// <summary>The linked thing was deleted or is not the owner's.</summary>
    public const string Gone = "gone";
}

public sealed record ProjectInput(string? Name, string? Description, string? Instructions, string? Color);

/// <summary>What Jarvis is told about the project a conversation belongs to.</summary>
public sealed record ProjectContext(Guid Id, string Name, string? Description, string? Instructions,
    IReadOnlyList<ProjectFileReference> Files, int TotalFiles, string? Outcome = null,
    ProjectNextAction? NextAction = null);

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

    /// <summary>Sets the outcome, status and next action. Throws <see cref="ArgumentException"/> for invalid input.</summary>
    Task<ProjectRecord?> SetFocusAsync(Guid projectId, Guid ownerId, ProjectFocusInput input,
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

/// <summary>
/// Sets a project's outcome and next action and describes the next action's current state. The next action points at an
/// existing commitment, decision, reminder or task; the service checks the owner owns it, and reads its state from the
/// owning feature so a project never keeps a second copy of an obligation.
/// </summary>
public interface IProjectFocusService
{
    /// <exception cref="ArgumentException">A field is invalid or the linked thing is not the owner's.</exception>
    Task<ProjectRecord?> SetAsync(Guid ownerId, Guid projectId, ProjectFocusInput input,
        CancellationToken cancellationToken);

    Task<ProjectNextActionView?> DescribeAsync(Guid ownerId, ProjectRecord project,
        CancellationToken cancellationToken);
}
