using Jarvis.Application.Profiles;
using Jarvis.Application.Projects;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WorkspaceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fe31");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fe32");
    private static readonly Guid RestrictedProfile = Guid.Parse("01996b8c-6000-7000-8000-00000000fe41");
    private static readonly Guid OpenProfile = Guid.Parse("01996b8c-6000-7000-8000-00000000fe42");
    private static readonly Guid Project = Guid.Parse("01996b8c-6000-7000-8000-00000000fe51");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class World
    {
        public readonly InMemorySettingsStore Store = new();
        public HashSet<Guid> Profiles = [RestrictedProfile, OpenProfile];
        public bool ProjectExists = true;

        public WorkspaceService Service => new(Store,
            Fake<IAssistantProfileService>.Create(("GetAsync", args =>
            {
                var id = (Guid)args[0]!;
                var owner = (Guid)args[1]!;
                if (owner != Owner || !Profiles.Contains(id)) return null;
                return new AssistantProfileRecord(id, owner, id == RestrictedProfile ? "Research analyst" : "Default", null, false, 4,
                    null, null, null, true, false, [], id == RestrictedProfile, id == RestrictedProfile ? [Guid.NewGuid()] : [],
                    null, null, null, null, id == RestrictedProfile ? "pinned" : "all", true, true, true, id != RestrictedProfile, Now, Now);
            })),
            Fake<IProjectStore>.Create(("GetAsync", args =>
                ProjectExists && (Guid)args[0]! == Project && (Guid)args[1]! == Owner
                    ? new ProjectRecord(Project, "Renovation", null, null, "blue", Now, Now, 0, 0, 0)
                    : null)),
            new Clock());
    }

    private static WorkspaceInput Input(Guid? profile = null, Guid? project = null, string? name = "Weekly research",
        string? instruction = "Summarise what changed") => new(name, profile ?? RestrictedProfile, project ?? Project, instruction);

    [Fact]
    public async Task A_workspace_carries_only_its_profiles_own_scope()
    {
        var world = new World();
        var saved = (await world.Service.SaveAsync(Owner, null, Input(), default)).Value!;

        Assert.Equal("Research analyst", saved.ProfileName);
        Assert.Equal("Renovation", saved.ProjectName);
        Assert.Equal("Only pinned memories", saved.Scope!.Memory);
        Assert.Equal("1 document collection", saved.Scope.Files);
        Assert.Contains("Cannot save new memories.", saved.Scope.Limits);
        // The record has no field for files, skills or memory: nothing a workspace stores can widen its profile.
        Assert.DoesNotContain(typeof(Workspace).GetProperties(), p => p.Name.Contains("File") || p.Name.Contains("Collection") || p.Name.Contains("Skill") || p.Name.Contains("Memory"));
    }

    [Fact]
    public async Task Launching_returns_what_to_start_and_marks_it_used()
    {
        var world = new World();
        var saved = (await world.Service.SaveAsync(Owner, null, Input(), default)).Value!;

        var launch = await world.Service.LaunchAsync(Owner, saved.Workspace.Id, default);

        Assert.True(launch.Succeeded);
        Assert.Equal(RestrictedProfile, launch.Value!.ProfileId);
        Assert.Equal(Project, launch.Value.ProjectId);
        Assert.Equal("Summarise what changed", launch.Value.OpeningMessage);
        Assert.Equal("Research analyst", launch.Value.Scope.Name);
        Assert.NotNull((await world.Service.ListAsync(Owner, default)).Single().Workspace.LastUsedAt);
    }

    [Fact]
    public async Task A_deleted_profile_or_project_blocks_launch_and_says_why()
    {
        var world = new World();
        var saved = (await world.Service.SaveAsync(Owner, null, Input(), default)).Value!;

        world.ProjectExists = false;
        var view = (await world.Service.ListAsync(Owner, default)).Single();
        Assert.Contains("project was deleted", view.Problem);
        Assert.Equal("workspace", (await world.Service.LaunchAsync(Owner, saved.Workspace.Id, default)).Field);

        world.ProjectExists = true;
        world.Profiles.Remove(RestrictedProfile);
        Assert.Contains("profile was deleted", (await world.Service.ListAsync(Owner, default)).Single().Problem);
        Assert.Equal("workspace", (await world.Service.LaunchAsync(Owner, saved.Workspace.Id, default)).Field);
    }

    [Fact]
    public async Task Input_is_validated_and_must_belong_to_the_owner()
    {
        var world = new World();

        Assert.Equal("name", (await world.Service.SaveAsync(Owner, null, Input(name: " "), default)).Field);
        Assert.Equal("name", (await world.Service.SaveAsync(Owner, null, Input(name: new string('a', 61)), default)).Field);
        Assert.Equal("instruction", (await world.Service.SaveAsync(Owner, null, Input(instruction: new string('a', 401)), default)).Field);
        Assert.Equal("profileId", (await world.Service.SaveAsync(Owner, null, Input(profile: Guid.NewGuid()), default)).Field);
        Assert.Equal("profileId", (await world.Service.SaveAsync(Other, null, Input(), default)).Field);
        Assert.Equal("projectId", (await world.Service.SaveAsync(Owner, null, Input(project: Guid.NewGuid()), default)).Field);
    }

    [Fact]
    public async Task Workspaces_can_be_edited_deleted_and_are_private_and_bounded()
    {
        var world = new World();
        var first = (await world.Service.SaveAsync(Owner, null, Input(), default)).Value!;

        var edited = await world.Service.SaveAsync(Owner, first.Workspace.Id, Input(profile: OpenProfile, name: "Renamed"), default);
        Assert.Equal("Renamed", edited.Value!.Workspace.Name);
        Assert.Equal(OpenProfile, edited.Value.Workspace.ProfileId);
        Assert.True((await world.Service.SaveAsync(Owner, Guid.NewGuid(), Input(), default)).NotFound);

        Assert.Empty(await world.Service.ListAsync(Other, default));
        Assert.False(await world.Service.DeleteAsync(Other, first.Workspace.Id, default));
        Assert.True(await world.Service.DeleteAsync(Owner, first.Workspace.Id, default));

        for (var i = 0; i < WorkspaceRules.MaxWorkspaces; i++)
            Assert.True((await world.Service.SaveAsync(Owner, null, Input(name: $"W{i}"), default)).Succeeded);
        Assert.Equal("workspace", (await world.Service.SaveAsync(Owner, null, Input(name: "One more"), default)).Field);
    }
}
