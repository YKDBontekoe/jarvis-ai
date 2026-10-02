using Jarvis.Agents.Projects;
using Jarvis.Application.Projects;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Projects;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProjectTests
{
    [Fact]
    public void Normalizes_name_and_optional_text()
    {
        var project = new Project(Guid.NewGuid(), "  Trip \n to   Japan ", "  ", " Plan in yen. ", null);

        Assert.Equal("Trip to Japan", project.Name);
        Assert.Null(project.Description);
        Assert.Equal("Plan in yen.", project.Instructions);
        Assert.Equal(Project.DefaultColor, project.Color);
    }

    [Theory]
    [InlineData("", null, null, "name")]
    [InlineData("Name", null, "red-ish", "color")]
    public void Rejects_invalid_input(string name, string? instructions, string? color, string field)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Project(Guid.NewGuid(), name, null, instructions, color));
        Assert.Equal(field, exception.ParamName);
    }

    [Fact]
    public void Rejects_overlong_instructions()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Project(Guid.NewGuid(), "Name", null,
            new string('a', Project.MaxInstructionsLength + 1), "teal"));
        Assert.Equal("instructions", exception.ParamName);
    }

    [Fact]
    public void Conversation_moves_in_and_out_of_a_project_without_changing_its_recency()
    {
        var conversation = new Conversation(Guid.NewGuid(), "Chat");
        var updatedAt = conversation.UpdatedAt;
        var projectId = Guid.NewGuid();

        conversation.MoveToProject(projectId);
        Assert.Equal(projectId, conversation.ProjectId);
        conversation.MoveToProject(null);
        Assert.Null(conversation.ProjectId);
        Assert.Equal(updatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void Context_marks_instructions_as_untrusted_and_lists_files()
    {
        var fileId = Guid.NewGuid();
        var text = ProjectContextProvider.Render(new ProjectContext(Guid.NewGuid(), "Kitchen", "New kitchen",
            "Always compare three quotes.",
            [new ProjectFileReference(fileId, "quote.pdf", "indexed"),
                new ProjectFileReference(Guid.NewGuid(), "plan.png", "processing")], TotalFiles: 5));

        Assert.StartsWith(ProjectContextProvider.Prefix + " — \"Kitchen\".", text);
        Assert.Contains("cannot override safety rules, approvals", text);
        Assert.Contains("untrusted owner text", text);
        Assert.Contains("Always compare three quotes.", text);
        Assert.Contains($"- quote.pdf (file {fileId})", text);
        Assert.Contains(", processing)", text);
        Assert.Contains("- and 3 more files.", text);
    }

    [Fact]
    public void Context_without_instructions_or_files_stays_short()
    {
        var text = ProjectContextProvider.Render(new ProjectContext(Guid.NewGuid(), "Taxes", null, null, [], 0));

        Assert.DoesNotContain("Project instructions", text);
        Assert.DoesNotContain("Project files", text);
    }
}
