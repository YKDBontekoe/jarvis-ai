using Jarvis.Application.Skills;
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

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
