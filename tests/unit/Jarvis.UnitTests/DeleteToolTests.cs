using Jarvis.Agents;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DeleteToolTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class User : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    [Fact]
    public async Task Deleting_a_file_is_scoped_to_the_owner_and_reports_a_miss()
    {
        (Guid, Guid)? seen = null;
        var found = true;
        var files = Fake<IFileService>.Create(("DeleteAsync", args => { seen = ((Guid)args[0]!, (Guid)args[1]!); return found; }));
        var tools = new FileDeletionTools(files, new User());
        var id = Guid.NewGuid();

        Assert.Equal("The file is deleted.", await tools.DeleteFileAsync(id.ToString()));
        Assert.Equal((id, Owner), seen);
        found = false;
        Assert.Equal("I could not find that file.", await tools.DeleteFileAsync(id.ToString()));
        Assert.StartsWith("fileId must be an id", await tools.DeleteFileAsync("../etc"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_delete_tool_needs_the_users_approval_and_the_file_one_is_chat_only()
    {
        var contributor = new FileDeletionToolContributor(Fake<IFileService>.Create(), new User());

        var chat = contributor.GetTools(new AgentBuildContext(Owner, null)).ToArray();
        var tool = Assert.Single(chat);
        Assert.IsType<ApprovalRequiredAIFunction>(tool);
        Assert.Equal("DeleteFile", tool.Name);
        Assert.Empty(contributor.GetTools(new AgentBuildContext(Owner, Guid.NewGuid())));
    }

    [Theory]
    [InlineData("DeleteFile", "Please delete the file budget.pdf")]
    [InlineData("DeleteFile", "verwijder dat bestand")]
    public void Asking_to_delete_a_file_offers_the_tool_but_other_talk_does_not(string tool, string text)
    {
        var group = ToolGroups.All.Single(item => item.Tools.Contains(tool));
        Assert.Matches(group.Keywords, text);
        Assert.DoesNotMatch(group.Keywords, "What is in my file budget.pdf?");
    }

    [Fact]
    public void Delete_tools_for_goals_habits_and_projects_are_listed_in_their_groups()
    {
        Assert.Contains("DeleteGoal", ToolGroups.All.Single(g => g.Id == "goals").Tools);
        Assert.Contains("DeleteHabit", ToolGroups.All.Single(g => g.Id == "habits").Tools);
        Assert.Contains("DeleteProject", ToolGroups.All.Single(g => g.Id == "projects").Tools);
    }

    [Fact]
    public async Task Deleting_a_project_is_scoped_to_the_owner_needs_approval_and_keeps_its_contents()
    {
        (Guid, Guid)? seen = null;
        var store = Fake<Jarvis.Application.Projects.IProjectStore>.Create(("DeleteAsync", args =>
        {
            seen = ((Guid)args[0]!, (Guid)args[1]!);
            return true;
        }));
        var focus = Fake<Jarvis.Application.Projects.IProjectFocusService>.Create();
        var id = Guid.NewGuid();
        var contributor = new Jarvis.Agents.Projects.ProjectToolContributor(store, focus, new User());

        var reply = await new Jarvis.Agents.Projects.ProjectAgentTools(store, focus, new User()).DeleteProjectAsync(id.ToString());

        Assert.Contains("kept", reply, StringComparison.Ordinal);
        Assert.Equal((id, Owner), seen);
        Assert.IsType<ApprovalRequiredAIFunction>(contributor.GetTools(new AgentBuildContext(Owner, null)).Single(t => t.Name == "DeleteProject"));
        Assert.StartsWith("projectId must be an id", await new Jarvis.Agents.Projects.ProjectAgentTools(store, focus, new User()).DeleteProjectAsync("x"), StringComparison.Ordinal);
    }
}
