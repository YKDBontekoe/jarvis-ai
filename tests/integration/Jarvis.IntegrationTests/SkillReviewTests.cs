using Jarvis.Application.Improvements;
using Jarvis.Application.Skills;
using Jarvis.Domain.Improvements;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class SkillReviewTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    private static SkillDraft Draft(string name, string body) =>
        new(name, "Use when planning trips.", body);

    private async Task<SkillRecord> Save(Guid owner, SkillDraft draft, string source, string status, bool respectLock = true)
    {
        await using var database = CreateDbContext();
        return (await new SkillRepository(database).UpsertAsync(owner, draft, source, status, respectLock, "test", default))!;
    }

    [Fact]
    public async Task A_learned_rewrite_of_an_active_skill_goes_back_to_review_when_review_is_on()
    {
        var owner = Guid.CreateVersion7();
        var first = await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Active);
        Assert.Equal(SkillStatuses.Active, first.Status);

        var rewritten = await Save(owner, Draft("trip-planning", "1. Ask for dates.\n2. Prefer trains."),
            SkillSources.Learned, SkillStatuses.Proposed);

        Assert.Equal((SkillStatuses.Proposed, 2), (rewritten.Status, rewritten.Version));
        await using var read = CreateDbContext();
        var revisions = await new SkillRepository(read).ListRevisionsAsync(rewritten.Id, owner, default);
        Assert.Equal([2, 1], revisions.Select(revision => revision.Version));
        Assert.Contains("budget", revisions[1].Instructions);
    }

    [Fact]
    public async Task With_auto_activate_on_a_learned_rewrite_stays_active()
    {
        var owner = Guid.CreateVersion7();
        await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Active);

        var rewritten = await Save(owner, Draft("trip-planning", "1. Ask for dates.\n2. Prefer trains."),
            SkillSources.Learned, SkillStatuses.Active);

        Assert.Equal((SkillStatuses.Active, 2), (rewritten.Status, rewritten.Version));
    }

    [Fact]
    public async Task The_same_text_changes_nothing_and_a_disabled_skill_stays_off()
    {
        var owner = Guid.CreateVersion7();
        var created = await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Active);
        var same = await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Proposed);
        Assert.Equal((SkillStatuses.Active, 1), (same.Status, same.Version));

        await using (var database = CreateDbContext())
            await new SkillRepository(database).SetStatusAsync(created.Id, owner, SkillStatuses.Disabled, default);
        var rewritten = await Save(owner, Draft("trip-planning", "1. Something new entirely."), SkillSources.Learned, SkillStatuses.Proposed);

        Assert.Equal(SkillStatuses.Disabled, rewritten.Status);
    }

    [Fact]
    public async Task The_owners_own_edits_and_accepted_proposals_keep_a_skill_active()
    {
        var owner = Guid.CreateVersion7();
        await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Active);

        var edited = await Save(owner, Draft("trip-planning", "1. Owner's version."), SkillSources.User, SkillStatuses.Active,
            respectLock: false);

        Assert.Equal((SkillStatuses.Active, true), (edited.Status, edited.IsLocked));
        await using var database = CreateDbContext();
        Assert.Null(await new SkillRepository(database).UpsertAsync(owner, Draft("trip-planning", "1. Agent's attempt."),
            SkillSources.Learned, SkillStatuses.Proposed, respectLock: true, "test", default));
    }

    [Fact]
    public async Task Outcome_counts_add_up_and_start_over_when_the_text_changes()
    {
        var owner = Guid.CreateVersion7();
        var skill = await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Active);
        var counted = DateTimeOffset.UtcNow.AddHours(-6);

        await using (var database = CreateDbContext())
        {
            var repository = new SkillRepository(database);
            await repository.AddOutcomesAsync(owner, new Dictionary<Guid, SkillOutcomeDelta> { [skill.Id] = new(4, 3, counted) }, default);
            await repository.AddOutcomesAsync(owner, new Dictionary<Guid, SkillOutcomeDelta>
            {
                [skill.Id] = new(2, 0, counted.AddHours(1)), [Guid.CreateVersion7()] = new(9, 9, counted)
            }, default);
        }

        await using (var read = CreateDbContext())
        {
            var totals = (await new SkillRepository(read).GetAsync(skill.Id, owner, default))!;
            Assert.Equal((6, 3), (totals.OutcomeRuns, totals.OutcomeSuccesses));
            Assert.Equal(counted.AddHours(1), totals.OutcomeCountedAt!.Value, TimeSpan.FromMilliseconds(1));
        }

        var rewritten = await Save(owner, Draft("trip-planning", "1. Ask for dates.\n2. Prefer trains."),
            SkillSources.Learned, SkillStatuses.Active);

        Assert.Equal((0, 0, 2), (rewritten.OutcomeRuns, rewritten.OutcomeSuccesses, rewritten.Version));
        Assert.True(rewritten.OutcomeCountedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task A_redraft_is_stored_applied_against_the_same_version_and_undone_from_the_revision_history()
    {
        var owner = Guid.CreateVersion7();
        var skill = await Save(owner, Draft("trip-planning", "1. Ask for dates and budget."), SkillSources.Learned, SkillStatuses.Active);
        var now = DateTimeOffset.UtcNow;
        var payload = System.Text.Json.JsonSerializer.Serialize(
            new SkillRefinePayload("trip-planning", "Use when planning trips.", "1. Check inputs.\n2. Ask for dates.", skill.Version),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var proposal = new ImprovementProposal(Guid.CreateVersion7(), owner, ImprovementKinds.SkillRefine,
            SkillRefineRules.Fingerprint(skill), "Improve “trip-planning”", "Went well 1 of 5 times.", 0.7, payload,
            ImprovementStatuses.Pending, null, now, now);
        await using (var database = CreateDbContext())
            await new ImprovementRepository(database).AddAsync(proposal, default);

        await using (var database = CreateDbContext())
        {
            var service = Service(database);
            var accepted = await service.AcceptAsync(proposal.Id, owner, default);
            Assert.Equal(ImprovementStatuses.Accepted, accepted!.Status);
        }

        await using (var read = CreateDbContext())
        {
            var rewritten = (await new SkillRepository(read).GetAsync(skill.Id, owner, default))!;
            Assert.Equal((2, SkillStatuses.Active), (rewritten.Version, rewritten.Status));
            Assert.Contains("Check inputs", rewritten.Instructions);
        }

        await using (var database = CreateDbContext())
            Assert.True(await Service(database).UndoAsync(proposal.Id, owner, default));

        await using var after = CreateDbContext();
        var restored = (await new SkillRepository(after).GetAsync(skill.Id, owner, default))!;
        Assert.Equal((3, "1. Ask for dates and budget."), (restored.Version, restored.Instructions));
    }

    [Fact]
    public async Task Required_tools_are_stored_and_a_tools_only_change_makes_no_new_revision()
    {
        var owner = Guid.CreateVersion7();
        var created = await Save(owner, Draft("train-check", "1. Call get_departures.") with
        {
            RequiredTools = ["get_departures"]
        }, SkillSources.Learned, SkillStatuses.Active);
        Assert.Equal(["get_departures"], created.RequiredTools);

        // No tools in the draft keeps the stored ones.
        var kept = await Save(owner, Draft("train-check", "1. Call get_departures.\n2. Mention delays."),
            SkillSources.Learned, SkillStatuses.Active);
        Assert.Equal(2, kept.Version);
        Assert.Equal(["get_departures"], kept.RequiredTools);

        var retooled = await Save(owner, Draft("train-check", "1. Call get_departures.\n2. Mention delays.") with
        {
            RequiredTools = ["get_departures", "get_delays"]
        }, SkillSources.Learned, SkillStatuses.Active);
        Assert.Equal(2, retooled.Version);
        Assert.Equal(["get_departures", "get_delays"], retooled.RequiredTools);

        await using var read = CreateDbContext();
        Assert.Equal(["get_departures", "get_delays"],
            (await new SkillRepository(read).GetAsync(created.Id, owner, default))!.RequiredTools);
        Assert.Equal(2, (await new SkillRepository(read).ListRevisionsAsync(created.Id, owner, default)).Count);
    }

    [Fact]
    public async Task Capability_proposals_are_allowed_by_the_database()
    {
        var owner = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await using (var database = CreateDbContext())
            await new ImprovementRepository(database).AddAsync(new ImprovementProposal(Guid.CreateVersion7(), owner,
                ImprovementKinds.Capability, "gap:live-train-departures", "Jarvis keeps lacking: live train departures",
                "Needed in 2 conversations.", 0.6, """{"slug":"live-train-departures","reports":2,"conversations":2}""",
                ImprovementStatuses.Pending, null, now, now), default);

        await using var read = CreateDbContext();
        var stored = await new ImprovementRepository(read).FindByFingerprintAsync(owner, "gap:live-train-departures", default);
        Assert.Equal(ImprovementKinds.Capability, stored!.Kind);
    }

    // Only the repositories a refinement touches are real; the other collaborators are never reached.
    private static ImprovementService Service(JarvisDbContext database) =>
        new(new ImprovementRepository(database), null!, new SkillRepository(database), null!, null!, null!);

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
