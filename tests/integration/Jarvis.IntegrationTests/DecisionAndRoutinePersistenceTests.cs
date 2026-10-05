using Jarvis.Application.Automations;
using Jarvis.Application.Decisions;
using Jarvis.Application.Reviews;
using Jarvis.Application.Routines;
using Jarvis.Domain.Automations;
using Jarvis.Domain.Decisions;
using Jarvis.Domain.Routines;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class DecisionAndRoutinePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // ---- decisions ----

    [Fact]
    public async Task Decisions_are_stored_owner_scoped_and_listed_by_review_date()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new DecisionRepository(database);
        var now = DateTimeOffset.UtcNow;
        var soon = Decision(owner, "Soon", new DateOnly(2026, 11, 1), now);
        var later = Decision(owner, "Later", new DateOnly(2026, 12, 1), now);
        await repository.AddAsync(soon, default);
        await repository.AddAsync(later, default);
        await repository.AddAsync(Decision(other, "Theirs", new DateOnly(2026, 11, 5), now), default);

        Assert.Equal(["Later", "Soon"], (await repository.ListAsync(owner, default)).Select(x => x.Title));
        Assert.Single(await repository.ListAsync(other, default));
        Assert.Equal(2, await repository.CountUnresolvedAsync(owner, default));
        Assert.Null(await repository.GetAsync(soon.Id, other, default));

        var resolved = soon with { Outcome = true, OutcomeNote = "yes", ResolvedAt = now, UpdatedAt = now };
        Assert.True(await repository.UpdateAsync(resolved, default));
        Assert.False(await repository.UpdateAsync(resolved with { OwnerId = other }, default));
        var stored = (await repository.GetAsync(soon.Id, owner, default))!;
        Assert.True(stored.Outcome);
        Assert.Equal("yes", stored.OutcomeNote);
        Assert.True(stored.IsResolved);
        Assert.Equal(1, await repository.CountUnresolvedAsync(owner, default));

        Assert.False(await repository.DeleteAsync(later.Id, other, default));
        Assert.True(await repository.DeleteAsync(later.Id, owner, default));
        Assert.Single(await repository.ListAsync(owner, default));
    }

    [Fact]
    public async Task The_timeline_query_finds_decisions_logged_or_resolved_in_the_range()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new DecisionRepository(database);
        var day = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        await repository.AddAsync(Decision(owner, "Logged in range", new DateOnly(2026, 7, 1), day), default);
        await repository.AddAsync(Decision(owner, "Old", new DateOnly(2026, 7, 1), day.AddDays(-60)), default);
        await repository.AddAsync(Decision(owner, "Resolved in range", new DateOnly(2026, 6, 20), day.AddDays(-60)) with
        {
            Outcome = false, ResolvedAt = day.AddDays(2)
        }, default);

        var found = await repository.ListActiveBetweenAsync(owner, day.AddDays(-1), day.AddDays(5), 50, default);

        Assert.Equal(["Logged in range", "Resolved in range"], found.Select(x => x.Title).Order().ToArray());
        Assert.Empty(await repository.ListActiveBetweenAsync(Guid.CreateVersion7(), day.AddDays(-1),
            day.AddDays(5), 50, default));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.2)]
    public async Task The_database_rejects_a_probability_outside_one_to_ninety_nine_percent(double probability)
    {
        await using var database = CreateDbContext();
        var repository = new DecisionRepository(database);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(
            Decision(Guid.CreateVersion7(), "Bad", new DateOnly(2026, 11, 1), DateTimeOffset.UtcNow) with
            {
                Probability = probability
            }, default));
    }

    [Fact]
    public async Task The_weekly_review_scores_the_decisions_settled_that_week_against_earlier_ones()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var week = WeeklyReviewClock.CurrentWeekStart(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        var weekStart = new DateTimeOffset(week.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await using (var seed = CreateDbContext())
        {
            var repository = new DecisionRepository(seed);
            // This week: said 90% and it happened (0.01), said 90% and it did not (0.81).
            await repository.AddAsync(Settled(owner, "Right", 0.9, true, weekStart.AddDays(1)), default);
            await repository.AddAsync(Settled(owner, "Wrong", 0.9, false, weekStart.AddDays(2)), default);
            // Before the week: said 50% twice (0.25 each).
            await repository.AddAsync(Settled(owner, "Coin A", 0.5, true, weekStart.AddDays(-3)), default);
            await repository.AddAsync(Settled(owner, "Coin B", 0.5, false, weekStart.AddDays(-9)), default);
            // Not settled, and someone else's.
            await repository.AddAsync(Decision(owner, "Open", new DateOnly(2027, 1, 1), weekStart), default);
            await repository.AddAsync(Settled(other, "Theirs", 0.99, false, weekStart.AddDays(1)), default);
        }

        await using var database = CreateDbContext();
        var facts = await new WeeklyReviewRepository(database).CollectAsync(owner, week, "UTC", default);

        Assert.Equal(2, facts.Stats.DecisionsResolved);
        Assert.Equal(0.41, facts.Stats.BrierScore!.Value, 3);
        Assert.Equal(0.25, facts.Stats.PreviousBrierScore!.Value, 3);
        var none = await new WeeklyReviewRepository(database)
            .CollectAsync(Guid.CreateVersion7(), week, "UTC", default);
        Assert.Equal(0, none.Stats.DecisionsResolved);
        Assert.Null(none.Stats.BrierScore);
    }

    // ---- routine suggestions ----

    [Fact]
    public async Task Routine_suggestions_are_owner_scoped_and_unique_per_pattern()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new RoutineSuggestionRepository(database);
        var now = DateTimeOffset.UtcNow;
        var first = Suggestion(owner, "time:journal", now);
        await repository.AddRangeAsync([first, Suggestion(owner, "time:habit:stretch", now.AddMinutes(-1))], default);
        await repository.AddRangeAsync([Suggestion(other, "time:journal", now)], default); // same pattern, other owner

        Assert.Equal(2, (await repository.ListAsync(owner, default)).Count);
        Assert.Single(await repository.ListAsync(other, default));
        Assert.Null(await repository.GetAsync(first.Id, other, default));

        await using (var duplicate = CreateDbContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new RoutineSuggestionRepository(duplicate)
                .AddRangeAsync([Suggestion(owner, "time:journal", now)], default));

        var accepted = first with { Status = RoutineStatuses.Accepted, AutomationId = Guid.CreateVersion7(), UpdatedAt = now };
        Assert.True(await repository.UpdateAsync(accepted, default));
        Assert.False(await repository.UpdateAsync(accepted with { OwnerId = other }, default));
        var stored = (await repository.GetAsync(first.Id, owner, default))!;
        Assert.Equal(RoutineStatuses.Accepted, stored.Status);
        Assert.Equal(accepted.AutomationId, stored.AutomationId);

        Assert.Equal(0, await repository.DeleteManyAsync(other, [first.Id], default));
        Assert.Equal(1, await repository.DeleteManyAsync(owner, [first.Id], default));
        Assert.Single(await repository.ListAsync(owner, default));
    }

    [Fact]
    public async Task A_suggestion_keeps_a_definition_that_still_parses_after_a_round_trip_through_jsonb()
    {
        var owner = Guid.CreateVersion7();
        var definition = new AutomationRuleDefinition(AutomationSchema.CurrentVersion,
            new ScheduleTriggerDefinition(new TimeOnly(22, 10), "Europe/Amsterdam", 0b0011111), null,
            [new NotificationActionDefinition("Time to write in your journal", "You usually do this around now.")],
            null);
        await using var database = CreateDbContext();
        var repository = new RoutineSuggestionRepository(database);
        var entry = Suggestion(owner, "time:journal", DateTimeOffset.UtcNow) with
        {
            DefinitionJson = AutomationDefinitionJson.Serialize(definition)
        };
        await repository.AddRangeAsync([entry], default);

        await using var reader = CreateDbContext();
        var stored = (await new RoutineSuggestionRepository(reader).GetAsync(entry.Id, owner, default))!;

        var parsed = AutomationDefinitionJson.Parse(stored.DefinitionJson);
        var trigger = Assert.IsType<ScheduleTriggerDefinition>(parsed.Trigger);
        Assert.Equal(new TimeOnly(22, 10), trigger.LocalTime);
        Assert.Equal(0b0011111, trigger.Weekdays);
        Assert.Equal("Time to write in your journal", Assert.IsType<NotificationActionDefinition>(parsed.Actions[0]).Title);
    }

    [Fact]
    public async Task The_database_rejects_an_unknown_suggestion_status()
    {
        await using var database = CreateDbContext();
        var repository = new RoutineSuggestionRepository(database);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddRangeAsync(
            [Suggestion(Guid.CreateVersion7(), "time:x", DateTimeOffset.UtcNow) with { Status = "maybe" }], default));
    }

    private static Decision Decision(Guid owner, string title, DateOnly reviewOn, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), owner, title, null, "It works out", 0.7, reviewOn, null, null, null, null,
            createdAt, createdAt);

    private static Decision Settled(Guid owner, string title, double probability, bool outcome, DateTimeOffset at) =>
        Decision(owner, title, DateOnly.FromDateTime(at.UtcDateTime), at.AddDays(-10)) with
        {
            Probability = probability, Outcome = outcome, ResolvedAt = at
        };

    private static RoutineSuggestionEntry Suggestion(Guid owner, string fingerprint, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), owner, fingerprint, "Title", "Evidence", 0.8, "{}", RoutineStatuses.Pending, null,
            at, at);

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
