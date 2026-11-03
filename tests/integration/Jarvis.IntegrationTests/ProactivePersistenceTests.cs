using Jarvis.Application.Proactivity;
using Jarvis.Application.Settings;
using Jarvis.Infrastructure.Events;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Infrastructure.Proactivity;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Jarvis.Api.Endpoints;
using Jarvis.Application.Conversations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using System.Net.Http.Json;
using System.Net;
using DotNet.Testcontainers.Builders;
using Jarvis.Workflows;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Worker;

namespace Jarvis.IntegrationTests;

public sealed class ProactivePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(Ct);
        await using var db = Database();
        await db.Database.MigrateAsync(Ct);
    }
    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    [Fact]
    public async Task Durable_workflow_resumes_after_worker_restart_and_honors_pause_without_duplicate_checks()
    {
        await using var temporal = new ContainerBuilder("temporalio/temporal:1.9.1")
            .WithCommand("server", "start-dev", "--ip", "0.0.0.0")
            .WithPortBinding(7233, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(7233)).Build();
        await temporal.StartAsync(Ct);
        // An open TCP port can precede Temporal's gRPC frontend readiness during startup.
        TemporalClient client;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                client = await TemporalClient.ConnectAsync(new($"{temporal.Hostname}:{temporal.GetMappedPublicPort(7233)}"));
                break;
            }
            catch (InvalidOperationException) when (attempt < 39) { await Task.Delay(250, Ct); }
        }
        var owner = Guid.NewGuid();
        ProactiveCase value;
        await using (var db = Database())
            value = await Service(db).CreateAsync(owner, new("project", "Prepare comparison", "Keep the comparison moving",
                Enabled: true, Policy: new(QuietStart: 0, QuietEnd: 0),
                Steps: [new("measure", "Measure", [], Done: true), new("compare", "Compare", ["measure"])]), Ct);
        var input = new ProactiveCaseWorkflowInput(owner, value.Id);
        var queue = "proactive-test-" + Guid.NewGuid().ToString("N");
        var workflowId = ProactiveCaseWorkflow.Id(owner, value.Id);
        var activity = new PersistenceActivity(async request => {
            await using var db = Database();
            return await Service(db).CheckAsync(request.OwnerId, request.CaseId, Ct);
        });
        TemporalWorker Worker() => new(client, new TemporalWorkerOptions(queue)
            .AddWorkflow<ProactiveCaseWorkflow>().AddAllActivities(activity));
        async Task<ProactiveCase> Read()
        {
            await using var db = Database();
            return (await new ProactiveRepository(db).GetAsync(owner, value.Id, Ct))!;
        }
        async Task UntilChecks(int checks)
        {
            for (var i = 0; i < 300; i++)
            {
                if ((await Read()).ChecksToday == checks) return;
                await Task.Delay(100, Ct);
            }
            Assert.Fail("The durable check did not finish within thirty seconds.");
        }
        using (var first = Worker())
            await first.ExecuteAsync(async () => {
                await client.StartWorkflowAsync((ProactiveCaseWorkflow workflow) => workflow.RunAsync(input), new(workflowId, queue));
                await UntilChecks(1);
            }, Ct);
        // No worker exists here. The owner's explicit wake is delivered durably to the restarted worker.
        var current = await Read();
        await using (var db = Database())
            await Service(db).CommandAsync(owner, value.Id, "resume", current.Revision, Ct);
        var handle = client.GetWorkflowHandle<ProactiveCaseWorkflow>(workflowId);
        await handle.SignalAsync(workflow => workflow.WakeAsync());
        using (var restarted = Worker())
            await restarted.ExecuteAsync(async () => {
                await UntilChecks(2);
                var checkedValue = await Read();
                Assert.Equal("Compare", checkedValue.NextAction);
                Assert.True(checkedValue.Steps[0].Done);
                await using var db = Database();
                await Service(db).CommandAsync(owner, value.Id, "pause", checkedValue.Revision, Ct);
                await handle.SignalAsync(workflow => workflow.WakeAsync());
                await handle.GetResultAsync().WaitAsync(TimeSpan.FromSeconds(30), Ct);
            }, Ct);
        var paused = await Read();
        Assert.Equal(2, paused.ChecksToday);
        Assert.False(paused.Enabled);
        Assert.Null(paused.NextCheckAt);
    }

    private sealed class PersistenceActivity(Func<ProactiveCaseWorkflowInput, Task<ProactiveCase?>> check) : ProactiveCaseActivityContract
    {
        [Activity("CheckProactiveCase")]
        public override async Task<ProactiveCheckSchedule> CheckAsync(ProactiveCaseWorkflowInput input)
        {
            var value = await check(input);
            return new(value is { Enabled: true, Terminal: false, NextCheckAt: not null }, 15);
        }
    }

    [Fact]
    public async Task Cases_survive_a_new_context_merge_duplicates_and_keep_owners_isolated()
    {
        var owner = Guid.NewGuid();
        Guid id;
        await using (var db = Database())
        {
            var service = Service(db);
            var first = await service.CreateAsync(owner, new("open_loop", "Compare", "Compare quotes"), Ct);
            var duplicate = await service.CreateAsync(owner, new("open_loop", "Compare again", "Compare quotes"), Ct);
            Assert.Equal(first.Id, duplicate.Id);
            id = first.Id;
        }
        await using var next = Database();
        var repository = new ProactiveRepository(next);
        Assert.Equal("Compare quotes", (await repository.GetAsync(owner, id, Ct))!.Objective);
        Assert.Null(await repository.GetAsync(Guid.NewGuid(), id, Ct));
        Assert.Single(await repository.ListAsync(owner, Ct));
    }

    [Fact]
    public async Task Revoking_profile_scope_removes_current_and_historical_preparation()
    {
        var owner = Guid.NewGuid();
        await using var db = Database();
        var profiles = new AssistantProfileRepository(db);
        var profile = await profiles.CreateAsync(owner, new("Work"), false, Ct);
        var project = new Jarvis.Domain.Projects.Project(owner, "Private project", null, null, null);
        db.Projects.Add(project);
        await db.SaveChangesAsync(Ct);
        var service = Service(db);
        var reference = "project:" + project.Id;
        var value = await service.CreateAsync(owner, new("context", "Resume", "Resume this project", [reference], ProfileId: profile.Id), Ct);
        var repository = new ProactiveRepository(db);
        var prepared = value with { Revision = value.Revision + 1, PreparedReport = "Private preparation",
            Reports = [new(DateTimeOffset.UtcNow, "Private preparation", new Dictionary<string, string> { [reference] = "v1" })] };
        Assert.True(await repository.CompareExchangeAsync(prepared, value.Revision, Ct));
        await profiles.UpdateAsync(profile.Id, owner, new("Work", MemoryScope: "profile", RestrictFiles: true), Ct);
        var restricted = (await service.GetAsync(owner, value.Id, Ct))!;
        Assert.Empty(restricted.Context);
        Assert.Null(restricted.PreparedReport);
        Assert.Null(restricted.Reports);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(owner,
            new("context", "Other", "Other objective", [reference], ProfileId: profile.Id), Ct));
    }

    [Fact]
    public async Task Only_one_concurrent_revision_can_win()
    {
        var owner = Guid.NewGuid();
        await using var db1 = Database();
        await using var db2 = Database();
        var first = await Service(db1).CreateAsync(owner, new("project", "Ship", "Ship release"), Ct);
        var repository1 = new ProactiveRepository(db1);
        var repository2 = new ProactiveRepository(db2);
        var results = await Task.WhenAll(
            repository1.CompareExchangeAsync(first with { Revision = 2, Status = "paused" }, 1, Ct),
            repository2.CompareExchangeAsync(first with { Revision = 2, Status = "resolved" }, 1, Ct));
        Assert.Single(results, won => won);
        Assert.Equal(2, (await repository1.GetAsync(owner, first.Id, Ct))!.Revision);
    }

    [Fact]
    public async Task Internal_plan_and_receipt_commit_together_and_intervening_edits_are_preserved()
    {
        var owner = Guid.NewGuid();
        await using var db = Database();
        var value = await Service(db).CreateAsync(owner, new("day_plan", "Today", "Keep today realistic", Enabled: true), Ct);
        var settings = new OwnerSettingsStore(db);
        await settings.SaveAsync(owner, "planner.day", new { marker = "owner edit" }, Ct);
        var repository = new ProactiveRepository(db);
        var changed = value with { Revision = value.Revision + 1, PreparedReport = "Replanned" };
        Assert.False(await repository.ApplyAsync(changed, value.Revision,
            new("planner.day", "{\"marker\":\"old\"}", "{\"marker\":\"automatic\"}"), Ct));
        Assert.Contains("owner edit", (await repository.ReadDocumentAsync(owner, "planner.day", Ct))!);
        Assert.Equal(value.Revision, (await repository.GetAsync(owner, value.Id, Ct))!.Revision);
        Assert.True(await repository.ApplyAsync(changed, value.Revision,
            new("planner.day", "{\"marker\":\"owner edit\"}", "{\"marker\":\"automatic\"}"), Ct));
        Assert.Contains("automatic", (await repository.ReadDocumentAsync(owner, "planner.day", Ct))!);
        Assert.Equal(changed.Revision, (await repository.GetAsync(owner, value.Id, Ct))!.Revision);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ApplyAsync(changed, changed.Revision,
            new("autonomy", null, "{}"), Ct));
    }

    [Fact]
    public async Task Foreign_sources_are_rejected_and_deleting_a_source_removes_it_from_context()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using var db = Database();
        var project = new Jarvis.Domain.Projects.Project(owner, "Renovation", null, null, null);
        db.Projects.Add(project);
        await db.SaveChangesAsync(Ct);
        var service = Service(db);
        var reference = "project:" + project.Id;
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(other,
            new("context", "Private", "Read their project", [reference]), Ct));
        var value = await service.CreateAsync(owner, new("context", "Context", "Resume renovation", [reference]), Ct);
        Assert.Single(value.Context);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(Ct);
        Assert.Empty((await service.GetAsync(owner, value.Id, Ct))!.Context);
    }

    [Fact]
    public async Task Running_http_routes_create_read_pause_and_reject_foreign_or_stale_commands()
    {
        var owner = Guid.NewGuid();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<JarvisDbContext>(options => options.UseNpgsql(postgres.GetConnectionString(), pg => pg.UseVector()));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser>(sp => new HeaderOwner(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), owner));
        builder.Services.AddScoped<IProactiveService>(sp => Service(sp.GetRequiredService<JarvisDbContext>()));
        builder.Services.AddScoped<IProactiveSourceReader>(sp => new ProactiveSourceReader(sp.GetRequiredService<JarvisDbContext>(),
            new AssistantProfileRepository(sp.GetRequiredService<JarvisDbContext>()),
            new RelatedEntityService(sp.GetRequiredService<JarvisDbContext>(), new EntityLinkRepository(sp.GetRequiredService<JarvisDbContext>())),
            new OwnerSettingsStore(sp.GetRequiredService<JarvisDbContext>())));
        await using var app = builder.Build();
        app.MapGroup("/api/v1").MapProactiveEndpoints();
        await app.StartAsync(Ct);
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single()) };
        var created = await http.PostAsJsonAsync("/api/v1/proactivity/cases", new ProactiveCaseDraft("open_loop", "Compare offers", "Compare the offers later"), Ct);
        created.EnsureSuccessStatusCode();
        var value = (await created.Content.ReadFromJsonAsync<ProactiveCase>(Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/v1/proactivity/cases/{value.Id}", Ct)).StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Owner", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/v1/proactivity/cases/{value.Id}", Ct)).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Test-Owner");
        var enabled = await http.PostAsJsonAsync($"/api/v1/proactivity/cases/{value.Id}/commands", new ProactiveCommandRequest("enable", value.Revision), Ct);
        enabled.EnsureSuccessStatusCode();
        var current = (await enabled.Content.ReadFromJsonAsync<ProactiveCase>(Ct))!;
        var stale = await http.PostAsJsonAsync($"/api/v1/proactivity/cases/{value.Id}/commands", new ProactiveCommandRequest("pause", value.Revision), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var paused = await http.PostAsJsonAsync($"/api/v1/proactivity/cases/{value.Id}/commands", new ProactiveCommandRequest("pause", current.Revision), Ct);
        paused.EnsureSuccessStatusCode();
        Assert.False((await paused.Content.ReadFromJsonAsync<ProactiveCase>(Ct))!.Enabled);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "tests/e2e/proactivity_flow.mjs"))) root = root.Parent;
        Assert.NotNull(root);
        var start = new System.Diagnostics.ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tests/e2e/proactivity_flow.mjs"));
        start.Environment["JARVIS_API_URL"] = http.BaseAddress!.ToString();
        using var flow = System.Diagnostics.Process.Start(start)!;
        var output = flow.StandardOutput.ReadToEndAsync(Ct);
        var errors = flow.StandardError.ReadToEndAsync(Ct);
        await flow.WaitForExitAsync(Ct);
        Assert.True(flow.ExitCode == 0, await output + await errors);
        await app.StopAsync(Ct);
    }

    private sealed class HeaderOwner(Microsoft.AspNetCore.Http.IHttpContextAccessor context, Guid fallback) : ICurrentUser
    {
        public Guid OwnerId => Guid.TryParse(context.HttpContext?.Request.Headers["X-Test-Owner"], out var owner) ? owner : fallback;
    }

    private static ProactiveService Service(JarvisDbContext db)
    {
        var settings = new OwnerSettingsStore(db);
        var related = new RelatedEntityService(db, new EntityLinkRepository(db), settings);
        return new(new ProactiveRepository(db), new ProactiveSourceReader(db, new AssistantProfileRepository(db), related, settings), settings);
    }

    private JarvisDbContext Database() => new(new DbContextOptionsBuilder<JarvisDbContext>()
        .UseNpgsql(postgres.GetConnectionString(), pg => pg.UseVector()).Options);
}
