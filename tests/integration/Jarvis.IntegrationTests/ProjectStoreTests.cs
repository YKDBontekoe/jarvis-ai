using Jarvis.Application.Projects;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class ProjectStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Groups_chats_files_and_tasks_and_keeps_them_when_the_project_is_deleted()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var chat = new Conversation(owner, "Kitchen plans");
        var taskConversation = new Conversation(owner, "Compare quotes");
        var task = new JarvisTask(owner, "Compare quotes", "Compare the three quotes");
        task.AttachConversation(taskConversation.Id);
        var file = File(owner, "quote.pdf");
        var foreignFile = File(other, "secret.pdf");
        await using (var seed = CreateDbContext())
        {
            seed.Conversations.AddRange(chat, taskConversation);
            seed.Tasks.Add(task);
            seed.Files.AddRange(file, foreignFile);
            await seed.SaveChangesAsync();
        }

        await using var database = CreateDbContext();
        var projects = new ProjectStore(database);
        var project = await projects.CreateAsync(owner,
            new ProjectInput("  Kitchen   renovation ", "New kitchen", "Answer in Dutch.", "green"),
            CancellationToken.None);
        Assert.Equal("Kitchen renovation", project.Name);

        Assert.Equal(ProjectAssignResult.Assigned,
            await projects.AssignConversationAsync(chat.Id, project.Id, owner, CancellationToken.None));
        Assert.Equal(ProjectAssignResult.Assigned,
            await projects.AssignFileAsync(file.Id, project.Id, owner, CancellationToken.None));
        Assert.Equal(ProjectAssignResult.Assigned,
            await projects.AssignTaskAsync(task.Id, project.Id, owner, CancellationToken.None));
        Assert.Equal(ProjectAssignResult.ItemNotFound,
            await projects.AssignFileAsync(foreignFile.Id, project.Id, owner, CancellationToken.None));
        Assert.Equal(ProjectAssignResult.ProjectNotFound,
            await projects.AssignConversationAsync(chat.Id, Guid.CreateVersion7(), owner, CancellationToken.None));

        var listed = Assert.Single(await projects.ListAsync(owner, CancellationToken.None));
        Assert.Equal((1, 1, 1), (listed.ConversationCount, listed.FileCount, listed.TaskCount));
        Assert.Empty(await projects.ListAsync(other, CancellationToken.None));
        Assert.Null(await projects.GetContentsAsync(project.Id, other, CancellationToken.None));

        var contents = await projects.GetContentsAsync(project.Id, owner, CancellationToken.None);
        Assert.Equal([chat.Id], contents!.Conversations.Select(x => x.Id));
        Assert.Equal([file.Id], contents.Files.Select(x => x.Id));
        Assert.Equal([task.Id], contents.Tasks.Select(x => x.Id));

        var context = await projects.GetContextForConversationAsync(taskConversation.Id, owner,
            CancellationToken.None);
        Assert.Equal("Answer in Dutch.", context!.Instructions);
        Assert.Equal(["quote.pdf"], context.Files.Select(x => x.FileName));
        Assert.Null(await projects.GetContextForConversationAsync(chat.Id, other, CancellationToken.None));

        Assert.False(await projects.DeleteAsync(project.Id, other, CancellationToken.None));
        Assert.True(await projects.DeleteAsync(project.Id, owner, CancellationToken.None));
        await using var check = CreateDbContext();
        Assert.Null((await check.Conversations.SingleAsync(x => x.Id == chat.Id)).ProjectId);
        Assert.Null((await check.Files.SingleAsync(x => x.Id == file.Id)).ProjectId);
        Assert.True(await check.Tasks.AnyAsync(x => x.Id == task.Id));
        Assert.Equal(2, await check.AuditEvents.CountAsync(x => x.OwnerId == owner && x.Tool == "projects"));
    }

    [Fact]
    public async Task Creates_a_task_inside_a_project_only_for_its_owner()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var project = await new ProjectStore(database).CreateAsync(owner, new ProjectInput("Taxes", null, null, null),
            CancellationToken.None);
        var tasks = new WorkflowRepository(database);

        var task = await tasks.CreateWithConversationAsync(owner, "File return", "File the return",
            CancellationToken.None, projectId: project.Id);
        Assert.Equal(project.Id, (await database.Conversations.SingleAsync(x => x.Id == task.ConversationId)).ProjectId);
        await Assert.ThrowsAsync<ArgumentException>(() => tasks.CreateWithConversationAsync(Guid.CreateVersion7(),
            "File return", "File the return", CancellationToken.None, projectId: project.Id));
    }

    private static StoredFileEntity File(Guid owner, string name) => new()
    {
        Id = Guid.CreateVersion7(), OwnerId = owner, ObjectKey = Guid.NewGuid().ToString(), FileName = name,
        ContentType = "application/pdf", SizeBytes = 10, Sha256 = new string('a', 64),
        CreatedAt = DateTimeOffset.UtcNow, ProcessingStatus = "indexed"
    };

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
