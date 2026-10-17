using Jarvis.Application.Decisions;
using Jarvis.Application.Events;
using Jarvis.Application.Inbox;
using Jarvis.Application.Projects;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.Projects;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProjectFocusTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid ProjectId = Guid.CreateVersion7();

    private static Commitment Promise(Guid id, string status = CommitmentStatuses.Open, Guid? owner = null) =>
        new(id, owner ?? Owner, CommitmentDirections.IOwe, "Rosa", "Send the quote", null, status, false,
            CommitmentSources.Chat, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ProjectFocusService Service(Commitment? commitment = null, ProjectFocusInput? captured = null,
        Action<ProjectFocusInput>? onSet = null) =>
        new(Fake<IProjectStore>.Create(("SetFocusAsync", args =>
            {
                var input = (ProjectFocusInput)args[2]!;
                onSet?.Invoke(input);
                return Record(input);
            })),
            Fake<ICommitmentService>.Create(("GetAsync", args =>
                commitment is { } found && found.Id == (Guid)args[0]! && found.OwnerId == (Guid)args[1]! ? found : null)),
            Fake<IDecisionService>.Create(("GetAsync", _ => null)),
            Fake<IReminderService>.Create(("GetAsync", _ => null)),
            Fake<IJarvisTaskService>.Create(("ListAsync", _ => (IReadOnlyList<JarvisTaskRecord>)[])));

    private static ProjectRecord Record(ProjectFocusInput input) =>
        new(ProjectId, "Kitchen", null, null, "blue", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, 0,
            input.Outcome, input.Status ?? "active", input.NextAction);

    [Fact]
    public async Task Links_a_commitment_the_owner_has_and_reports_its_state()
    {
        var id = Guid.CreateVersion7();
        var service = Service(Promise(id));

        var project = await service.SetAsync(Owner, ProjectId,
            new ProjectFocusInput("Finish by June", null, new ProjectNextAction("commitment", id, "Today")),
            CancellationToken.None);
        var view = await service.DescribeAsync(Owner, project!, CancellationToken.None);

        Assert.Equal("Send the quote", view!.Title);
        Assert.Equal("Today", view.Note);
        Assert.Equal(ProjectNextActionStates.Open, view.State);
    }

    [Fact]
    public async Task A_finished_commitment_makes_the_next_action_done()
    {
        var id = Guid.CreateVersion7();
        var service = Service(Promise(id, CommitmentStatuses.Done));
        var project = Record(new ProjectFocusInput(null, null, new ProjectNextAction("commitment", id, null)));

        var view = await service.DescribeAsync(Owner, project, CancellationToken.None);

        Assert.Equal(ProjectNextActionStates.Done, view!.State);
    }

    [Fact]
    public async Task Another_owners_commitment_cannot_be_linked_or_read()
    {
        var id = Guid.CreateVersion7();
        var service = Service(Promise(id, owner: Guid.CreateVersion7()));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Owner, ProjectId,
            new ProjectFocusInput(null, null, new ProjectNextAction("commitment", id, null)), CancellationToken.None));
        Assert.Equal("nextAction", error.ParamName);

        var seen = await service.DescribeAsync(Owner,
            Record(new ProjectFocusInput(null, null, new ProjectNextAction("commitment", id, null))),
            CancellationToken.None);
        Assert.Equal(ProjectNextActionStates.Gone, seen!.State);
        Assert.Null(seen.Title);
    }

    [Theory]
    [InlineData("expense")]
    [InlineData("project")]
    [InlineData("nonsense")]
    public async Task Only_commitments_decisions_reminders_and_tasks_can_be_linked(string type)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SetAsync(Owner, ProjectId,
            new ProjectFocusInput(null, null, new ProjectNextAction(type, Guid.CreateVersion7(), null)),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_link_needs_both_a_type_and_an_id()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SetAsync(Owner, ProjectId,
            new ProjectFocusInput(null, null, new ProjectNextAction("commitment", null, null)),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_note_alone_is_a_next_action_that_is_never_gone()
    {
        var service = Service();
        var project = await service.SetAsync(Owner, ProjectId,
            new ProjectFocusInput(null, null, new ProjectNextAction(null, null, "Call the plumber")),
            CancellationToken.None);

        var view = await service.DescribeAsync(Owner, project!, CancellationToken.None);

        Assert.Equal("Call the plumber", view!.Note);
        Assert.Equal(ProjectNextActionStates.Open, view.State);
        Assert.Null(await service.DescribeAsync(Owner, Record(new ProjectFocusInput(null, null, null)),
            CancellationToken.None));
    }

    [Fact]
    public void The_domain_validates_outcome_status_and_note_and_keeps_the_status_when_none_is_given()
    {
        var project = new Project(Owner, "Kitchen", null, null, null);
        Assert.Equal(Project.StatusActive, project.Status);

        project.SetFocus("  Cook for ten by June ", Project.StatusCompleted, null, null, null);
        Assert.Equal("Cook for ten by June", project.Outcome);
        Assert.Equal(Project.StatusCompleted, project.Status);

        project.SetFocus(null, null, null, null, "  Call  ");
        Assert.Null(project.Outcome);
        Assert.Equal(Project.StatusCompleted, project.Status);
        Assert.Equal("Call", project.NextActionNote);

        Assert.Throws<ArgumentException>(() => project.SetFocus(new string('x', 301), null, null, null, null));
        Assert.Throws<ArgumentException>(() => project.SetFocus(null, "paused", null, null, null));
        Assert.Throws<ArgumentException>(() => project.SetFocus(null, null, "commitment", null, null));
    }

    [Fact]
    public void Linkable_types_are_real_entity_types()
    {
        Assert.All(ProjectFocusService.LinkableTypes, type => Assert.True(EntityTypes.IsValid(type)));
    }
}
