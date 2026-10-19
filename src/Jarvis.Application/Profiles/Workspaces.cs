using Jarvis.Application.Projects;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Profiles;

/// <summary>
/// A starter for a recurring job: one profile, optionally one project, and an opening instruction. It carries no
/// file, skill or memory scope of its own: those always come from the profile's snapshot when the chat starts, so a
/// workspace can never reach more than its profile allows.
/// </summary>
public sealed record Workspace(Guid Id, string Name, Guid ProfileId, Guid? ProjectId, string? Instruction,
    DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed class WorkspaceState
{
    public List<Workspace> Items { get; set; } = [];
}

public sealed record WorkspaceInput(string? Name, Guid? ProfileId, Guid? ProjectId, string? Instruction);

public sealed record WorkspaceView(Workspace Workspace, string? ProfileName, string? ProjectName, string? Problem,
    ProfileScopeSummary? Scope);

/// <summary>What the app needs to open the chat. The conversation itself is made with the existing endpoint.</summary>
public sealed record WorkspaceLaunch(Guid ProfileId, Guid? ProjectId, string Title, string? OpeningMessage,
    string ProfileName, string? ProjectName, ProfileScopeSummary Scope);

public static class WorkspaceRules
{
    public const int MaxWorkspaces = 12;
    public const int MaxName = 60;
    public const int MaxInstruction = 400;
}

public sealed record WorkspaceOperation<T>(T? Value, string? Field = null, string? Message = null, bool NotFound = false)
{
    public bool Succeeded => Field is null && !NotFound;
    public static WorkspaceOperation<T> Ok(T value) => new(value);
    public static WorkspaceOperation<T> Invalid(string field, string message) => new(default, field, message);
    public static WorkspaceOperation<T> Missing() => new(default, null, null, true);
}

public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceView>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<WorkspaceOperation<WorkspaceView>> SaveAsync(Guid ownerId, Guid? id, WorkspaceInput input,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<WorkspaceOperation<WorkspaceLaunch>> LaunchAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
}

public sealed class WorkspaceService(IOwnerSettingsStore settings, IAssistantProfileService profiles, IProjectStore projects,
    TimeProvider? timeProvider = null) : IWorkspaceService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<WorkspaceView>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var views = new List<WorkspaceView>();
        foreach (var item in state.Items.OrderByDescending(w => w.LastUsedAt ?? w.CreatedAt))
            views.Add(await ViewAsync(ownerId, item, cancellationToken));
        return views;
    }

    public async Task<WorkspaceOperation<WorkspaceView>> SaveAsync(Guid ownerId, Guid? id, WorkspaceInput input,
        CancellationToken cancellationToken)
    {
        var name = Clean(input.Name);
        if (name is null or { Length: > WorkspaceRules.MaxName })
            return WorkspaceOperation<WorkspaceView>.Invalid("name", $"Give it a name of up to {WorkspaceRules.MaxName} characters.");
        var instruction = Clean(input.Instruction);
        if (instruction is { Length: > WorkspaceRules.MaxInstruction })
            return WorkspaceOperation<WorkspaceView>.Invalid("instruction", $"Keep the instruction under {WorkspaceRules.MaxInstruction} characters.");
        if (input.ProfileId is not { } profileId || await profiles.GetAsync(profileId, ownerId, cancellationToken) is null)
            return WorkspaceOperation<WorkspaceView>.Invalid("profileId", "Choose one of your profiles.");
        if (input.ProjectId is { } projectId && await projects.GetAsync(projectId, ownerId, cancellationToken) is null)
            return WorkspaceOperation<WorkspaceView>.Invalid("projectId", "Choose one of your projects.");

        var state = await LoadAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        Workspace saved;
        if (id is { } existingId)
        {
            var index = state.Items.FindIndex(w => w.Id == existingId);
            if (index < 0) return WorkspaceOperation<WorkspaceView>.Missing();
            saved = state.Items[index] with { Name = name, ProfileId = profileId, ProjectId = input.ProjectId, Instruction = instruction };
            state.Items[index] = saved;
        }
        else
        {
            if (state.Items.Count >= WorkspaceRules.MaxWorkspaces)
                return WorkspaceOperation<WorkspaceView>.Invalid("workspace", $"You can keep up to {WorkspaceRules.MaxWorkspaces} workspaces. Remove one first.");
            saved = new Workspace(Guid.NewGuid(), name, profileId, input.ProjectId, instruction, now, null);
            state.Items.Add(saved);
        }

        await settings.SaveAsync(ownerId, SettingsSections.Workspaces, state, cancellationToken);
        return WorkspaceOperation<WorkspaceView>.Ok(await ViewAsync(ownerId, saved, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        if (state.Items.RemoveAll(w => w.Id == id) == 0) return false;
        await settings.SaveAsync(ownerId, SettingsSections.Workspaces, state, cancellationToken);
        return true;
    }

    public async Task<WorkspaceOperation<WorkspaceLaunch>> LaunchAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var index = state.Items.FindIndex(w => w.Id == id);
        if (index < 0) return WorkspaceOperation<WorkspaceLaunch>.Missing();
        var workspace = state.Items[index];
        var view = await ViewAsync(ownerId, workspace, cancellationToken);
        if (view.Problem is not null || view.Scope is null || view.ProfileName is null)
            return WorkspaceOperation<WorkspaceLaunch>.Invalid("workspace", view.Problem ?? "This workspace cannot start.");

        state.Items[index] = workspace with { LastUsedAt = clock.GetUtcNow() };
        await settings.SaveAsync(ownerId, SettingsSections.Workspaces, state, cancellationToken);
        return WorkspaceOperation<WorkspaceLaunch>.Ok(new WorkspaceLaunch(workspace.ProfileId, workspace.ProjectId,
            workspace.Name, workspace.Instruction, view.ProfileName, view.ProjectName, view.Scope));
    }

    private async Task<WorkspaceView> ViewAsync(Guid ownerId, Workspace workspace, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetAsync(workspace.ProfileId, ownerId, cancellationToken);
        var project = workspace.ProjectId is { } projectId ? await projects.GetAsync(projectId, ownerId, cancellationToken) : null;
        var problem = profile is null
            ? "Its profile was deleted. Choose another one."
            : workspace.ProjectId is not null && project is null
                ? "Its project was deleted. Choose another one or none."
                : null;
        return new WorkspaceView(workspace, profile?.Name, project?.Name, problem,
            profile is null ? null : ProfileGuidance.Summarise(profile));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text)
        ? null
        : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private async Task<WorkspaceState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<WorkspaceState>(ownerId, SettingsSections.Workspaces, cancellationToken) ?? new WorkspaceState();
}
