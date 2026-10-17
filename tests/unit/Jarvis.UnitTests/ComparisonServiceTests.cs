using Jarvis.Application.Comparisons;
using Jarvis.Application.Decisions;
using Jarvis.Application.Library;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ComparisonServiceTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    private sealed class MemoryRepository : IComparisonRepository
    {
        public readonly Dictionary<Guid, ComparisonRecord> Rows = [];
        public Task<IReadOnlyList<ComparisonRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComparisonRecord>>(Rows.Values.Where(r => r.OwnerId == ownerId).ToArray());
        public Task<ComparisonRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.TryGetValue(id, out var row) && row.OwnerId == ownerId ? row : null);
        public Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Values.Count(r => r.OwnerId == ownerId));
        public Task AddAsync(ComparisonRecord record, CancellationToken cancellationToken)
        {
            Rows[record.Id] = record;
            return Task.CompletedTask;
        }
        public Task<bool> UpdateAsync(ComparisonRecord record, CancellationToken cancellationToken)
        {
            var found = Rows.TryGetValue(record.Id, out var row) && row.OwnerId == record.OwnerId;
            if (found) Rows[record.Id] = record;
            return Task.FromResult(found);
        }
        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.TryGetValue(id, out var row) && row.OwnerId == ownerId && Rows.Remove(id));
    }

    private static ComparisonDefinition Boilers() => new("Boiler", "Which boiler?",
        [new ComparisonCriterion("price", "Price", 8), new ComparisonCriterion("quiet", "Noise", 2)],
        [
            new ComparisonOption("alpha", "Alpha", null, new Dictionary<string, ComparisonCell>()),
            new ComparisonOption("beta", "Beta", null, new Dictionary<string, ComparisonCell>())
        ]);

    private sealed class Harness
    {
        public readonly MemoryRepository Repository = new();
        public readonly List<NoteDraft> Notes = [];
        public readonly List<CreateDecisionRequest> Decisions = [];
        public IComparisonService Service => new ComparisonService(Repository,
            Fake<ILibraryService>.Create(("AddNoteAsync", args =>
            {
                Notes.Add((NoteDraft)args[1]!);
                return LibraryOperation<LibraryItem>.Ok(Saved());
            })),
            Fake<IDecisionService>.Create(("CreateAsync", args =>
            {
                var request = (CreateDecisionRequest)args[1]!;
                if (request.ReviewOn < new DateOnly(2026, 10, 17)) throw new ArgumentException("The review date cannot be in the past.", "reviewOn");
                Decisions.Add(request);
                return new DecisionView(Guid.CreateVersion7(), request.Title, request.Context, request.Prediction,
                    request.Probability, request.ReviewOn, "open", null, null, null, DateTimeOffset.UtcNow);
            })));

        private static LibraryItem Saved() => (LibraryItem)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LibraryItem));
    }

    [Fact]
    public async Task Create_trims_and_stores_the_comparison_for_its_owner_only()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateAsync(Owner, Boilers() with { Title = "  Boiler  " }, null, default);

        Assert.True(created.Succeeded);
        Assert.Equal("Boiler", created.Value!.Record.Definition.Title);
        Assert.Single(await harness.Service.ListAsync(Owner, default));
        Assert.Empty(await harness.Service.ListAsync(Other, default));
        Assert.Null(await harness.Service.GetAsync(created.Value.Record.Id, Other, default));
        Assert.False(await harness.Service.DeleteAsync(created.Value.Record.Id, Other, default));
    }

    [Fact]
    public async Task Create_ignores_a_forged_choice_and_rejects_bad_shapes_with_the_field()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateAsync(Owner,
            Boilers() with { ChosenOptionId = "alpha", DecisionId = Guid.CreateVersion7() }, null, default);
        Assert.Null(created.Value!.Record.Definition.DecisionId);
        Assert.Null(created.Value.Record.Definition.ChosenOptionId);

        var bad = await harness.Service.CreateAsync(Owner, Boilers() with { Title = " " }, null, default);
        Assert.Equal(ComparisonFailure.Invalid, bad.Failure);
        Assert.Equal("title", bad.Field);
    }

    [Fact]
    public async Task Setting_a_cell_by_name_recomputes_the_ranking_and_an_unknown_stays_unknown()
    {
        var harness = new Harness();
        var id = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;

        await harness.Service.SetCellAsync(id, Owner, "Alpha", "price", new ComparisonCell(9, "€1,900", "quote-a.pdf", new DateOnly(2026, 10, 1)), default);
        var after = await harness.Service.SetCellAsync(id, Owner, "beta", "Noise", new ComparisonCell(7), default);

        var rows = after.Value!.Result.Rows;
        Assert.Equal("alpha", rows[0].OptionId);
        Assert.Equal(9.0, rows[0].Score);
        Assert.Equal(7.0, rows[1].Score);
        Assert.Equal(.8, rows[0].Coverage);
        Assert.Equal(["Price"], rows[1].UnknownCriteria);
        Assert.Equal("quote-a.pdf",
            after.Value.Record.Definition.Options.Single(option => option.Id == "alpha").Cells["price"].Source);

        var cleared = await harness.Service.SetCellAsync(id, Owner, "alpha", "price", new ComparisonCell(null), default);
        Assert.Null(cleared.Value!.Result.Rows.Single(row => row.OptionId == "alpha").Score);
    }

    [Fact]
    public async Task Setting_a_cell_rejects_unknown_names_and_other_owners()
    {
        var harness = new Harness();
        var id = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;

        Assert.Equal("option", (await harness.Service.SetCellAsync(id, Owner, "Gamma", "price", new ComparisonCell(5), default)).Field);
        Assert.Equal("criterion", (await harness.Service.SetCellAsync(id, Owner, "Alpha", "colour", new ComparisonCell(5), default)).Field);
        Assert.Equal(ComparisonFailure.Invalid, (await harness.Service.SetCellAsync(id, Owner, "Alpha", "price", new ComparisonCell(11), default)).Failure);
        Assert.Equal(ComparisonFailure.NotFound, (await harness.Service.SetCellAsync(id, Other, "Alpha", "price", new ComparisonCell(5), default)).Failure);
    }

    [Fact]
    public async Task An_edit_cannot_drop_or_forge_the_decision_link()
    {
        var harness = new Harness();
        var id = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;
        var decided = await harness.Service.DecideAsync(id, Owner, "alpha", .7, new DateOnly(2026, 12, 1), default);

        var edited = await harness.Service.UpdateAsync(id, Owner,
            Boilers() with { Title = "Boiler v2", ChosenOptionId = "beta", DecisionId = null }, default);

        Assert.Equal("Boiler v2", edited.Value!.Record.Definition.Title);
        Assert.Equal("alpha", edited.Value.Record.Definition.ChosenOptionId);
        Assert.Equal(decided.Value!.DecisionId, edited.Value.Record.Definition.DecisionId);
    }

    [Fact]
    public async Task Deciding_logs_one_decision_with_the_scores_and_unknowns_and_repeating_it_creates_nothing_new()
    {
        var harness = new Harness();
        var id = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;
        await harness.Service.SetCellAsync(id, Owner, "alpha", "price", new ComparisonCell(9), default);

        var first = await harness.Service.DecideAsync(id, Owner, "alpha", .7, new DateOnly(2026, 12, 1), default);
        var again = await harness.Service.DecideAsync(id, Owner, "alpha", .7, new DateOnly(2026, 12, 1), default);

        Assert.True(first.Value!.Created);
        Assert.False(again.Value!.Created);
        Assert.Equal(first.Value.DecisionId, again.Value.DecisionId);
        var request = Assert.Single(harness.Decisions);
        Assert.Equal("Choose Alpha: Boiler", request.Title);
        Assert.Contains("9/10", request.Context);
        Assert.Contains("Still unknown: Noise", request.Context);
        Assert.Equal(.7, request.Probability);
    }

    [Fact]
    public async Task Choosing_a_different_option_later_conflicts_and_bad_inputs_are_explained()
    {
        var harness = new Harness();
        var id = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;
        await harness.Service.DecideAsync(id, Owner, "alpha", .7, new DateOnly(2026, 12, 1), default);

        Assert.Equal(ComparisonFailure.Conflict,
            (await harness.Service.DecideAsync(id, Owner, "beta", .6, new DateOnly(2026, 12, 1), default)).Failure);
        Assert.Equal("optionId", (await harness.Service.DecideAsync(id, Owner, "gamma", .6, new DateOnly(2026, 12, 1), default)).Field);
        Assert.Equal(ComparisonFailure.NotFound, (await harness.Service.DecideAsync(id, Other, "alpha", .6, new DateOnly(2026, 12, 1), default)).Failure);

        var fresh = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;
        var past = await harness.Service.DecideAsync(fresh, Owner, "alpha", .6, new DateOnly(2026, 1, 1), default);
        Assert.Equal(ComparisonFailure.Invalid, past.Failure);
        Assert.Equal("reviewOn", past.Field);
    }

    [Fact]
    public async Task Saving_to_the_library_sends_the_markdown_as_a_tagged_report_and_remembers_the_item()
    {
        var harness = new Harness();
        var id = (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Value!.Record.Id;

        var saved = await harness.Service.SaveToLibraryAsync(id, Owner, default);

        Assert.True(saved.Succeeded);
        var note = Assert.Single(harness.Notes);
        Assert.Equal(LibraryKinds.Report, note.Kind);
        Assert.Equal("Boiler", note.Title);
        Assert.Contains("comparison", note.Tags!);
        Assert.Contains("## Ranking", note.Content);
        Assert.Equal(saved.Value, harness.Repository.Rows[id].LibraryItemId);
        Assert.Equal(ComparisonFailure.NotFound, (await harness.Service.SaveToLibraryAsync(id, Other, default)).Failure);
    }

    [Fact]
    public async Task An_owner_keeps_a_bounded_number_of_comparisons()
    {
        var harness = new Harness();
        for (var index = 0; index < IComparisonService.MaxPerOwner; index++)
            Assert.True((await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Succeeded);

        Assert.Equal(ComparisonFailure.Conflict, (await harness.Service.CreateAsync(Owner, Boilers(), null, default)).Failure);
        Assert.True((await harness.Service.CreateAsync(Other, Boilers(), null, default)).Succeeded);
    }
}
