using Jarvis.Application.Comparisons;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ComparisonTests
{
    private static ComparisonCell At(double? score, string? note = null) => new(score, note);

    private static ComparisonOption Option(string id, string name, params (string Criterion, double? Score)[] cells) =>
        new(id, name, null, cells.ToDictionary(cell => cell.Criterion, cell => At(cell.Score)));

    private static ComparisonDefinition Definition(IReadOnlyList<ComparisonOption> options,
        params (string Id, int Weight)[] criteria) =>
        new("Boiler", "Which boiler?", criteria.Select(c => new ComparisonCriterion(c.Id, c.Id.ToUpperInvariant(), c.Weight)).ToArray(), options);

    [Fact]
    public void The_score_is_the_weighted_average_of_what_is_known()
    {
        var definition = Definition(
            [Option("a", "Alpha", ("price", 8), ("quiet", 4)), Option("b", "Beta", ("price", 5), ("quiet", 9))],
            ("price", 3), ("quiet", 1));

        var result = ComparisonCalculator.Evaluate(definition);

        Assert.Equal(7.0, result.Rows.Single(row => row.OptionId == "a").Score); // (3*8 + 1*4) / 4
        Assert.Equal(6.0, result.Rows.Single(row => row.OptionId == "b").Score); // (3*5 + 1*9) / 4
        Assert.Equal("a", result.LeaderId);
        Assert.Equal([1, 2], result.Rows.Select(row => row.Rank));
    }

    [Fact]
    public void An_unknown_is_left_out_and_lowers_coverage_it_is_never_a_zero()
    {
        var definition = Definition(
            [Option("a", "Alpha", ("price", 6), ("quiet", null)), Option("b", "Beta", ("price", 5), ("quiet", 5))],
            ("price", 1), ("quiet", 1));

        var result = ComparisonCalculator.Evaluate(definition);

        var alpha = result.Rows.Single(row => row.OptionId == "a");
        Assert.Equal(6.0, alpha.Score);
        Assert.Equal(.5, alpha.Coverage);
        Assert.Equal(["QUIET"], alpha.UnknownCriteria);
        Assert.False(alpha.Thin);
        Assert.Equal(1.0, result.Rows.Single(row => row.OptionId == "b").Coverage);
    }

    [Fact]
    public void An_option_with_nothing_known_has_no_score_and_ranks_last()
    {
        var definition = Definition(
            [Option("a", "Alpha"), Option("b", "Beta", ("price", 2))], ("price", 1));

        var rows = ComparisonCalculator.Evaluate(definition).Rows;

        Assert.Equal("b", rows[0].OptionId);
        Assert.Null(rows[1].Score);
        Assert.Equal(0, rows[1].Rank);
    }

    [Fact]
    public void Options_missing_most_of_the_weight_are_flagged_thin()
    {
        var definition = Definition(
            [Option("a", "Alpha", ("price", 9)), Option("b", "Beta", ("price", 5), ("quiet", 5), ("size", 5))],
            ("price", 1), ("quiet", 1), ("size", 1));

        var result = ComparisonCalculator.Evaluate(definition);

        Assert.True(result.Rows.Single(row => row.OptionId == "a").Thin);
        Assert.Contains(result.Warnings, warning => warning.Contains("rough guide"));
    }

    [Fact]
    public void Scores_within_the_margin_tie_and_the_tie_is_called_out_not_hidden()
    {
        var definition = Definition(
            [Option("b", "Beta", ("price", 7.0)), Option("a", "Alpha", ("price", 7.04)), Option("c", "Gamma", ("price", 3))],
            ("price", 1));

        var result = ComparisonCalculator.Evaluate(definition);

        Assert.Equal(["a", "b", "c"], result.Rows.Select(row => row.OptionId));
        Assert.Equal([1, 1, 3], result.Rows.Select(row => row.Rank));
        Assert.True(result.LeaderIsTied);
        Assert.Contains(result.Warnings, warning => warning.Contains("tied"));
    }

    [Fact]
    public void The_same_input_always_gives_the_same_ranking_whatever_order_options_are_listed_in()
    {
        var a = Option("a", "Alpha", ("price", 7), ("quiet", 6));
        var b = Option("b", "Beta", ("price", 7), ("quiet", 6));
        var c = Option("c", "Cedar", ("price", 2), ("quiet", 9));
        var first = ComparisonCalculator.Evaluate(Definition([a, b, c], ("price", 2), ("quiet", 1)));
        var second = ComparisonCalculator.Evaluate(Definition([c, b, a], ("price", 2), ("quiet", 1)));

        Assert.Equal(first.Rows.Select(row => (row.OptionId, row.Score, row.Rank)),
            second.Rows.Select(row => (row.OptionId, row.Score, row.Rank)));
    }

    [Fact]
    public void Changing_one_weight_changes_the_leader_and_sensitivity_shows_which_criterion_decides_it()
    {
        var options = new[]
        {
            Option("cheap", "Cheap", ("price", 9), ("quiet", 2)),
            Option("quiet", "Quiet", ("price", 4), ("quiet", 9))
        };
        var priceFirst = ComparisonCalculator.Evaluate(Definition(options, ("price", 6), ("quiet", 2)));
        var quietFirst = ComparisonCalculator.Evaluate(Definition(options, ("price", 1), ("quiet", 6)));

        Assert.Equal("cheap", priceFirst.LeaderId);
        Assert.Equal("quiet", quietFirst.LeaderId);
        // With "price" gone only "quiet" counts, so the quiet option would lead.
        var price = priceFirst.Sensitivity.Single(item => item.CriterionId == "price");
        Assert.True(price.ChangesLeader);
        Assert.Equal("Quiet", price.LeaderWithout);
        Assert.False(priceFirst.Sensitivity.Single(item => item.CriterionId == "quiet").ChangesLeader);
    }

    [Fact]
    public void Nothing_to_rank_explains_itself()
    {
        Assert.Contains(ComparisonCalculator.Evaluate(Definition([Option("a", "Alpha")])).Warnings,
            warning => warning.Contains("criterion"));
        var zero = ComparisonCalculator.Evaluate(Definition([Option("a", "Alpha", ("price", 5))], ("price", 0)));
        Assert.Null(zero.LeaderId);
        Assert.Contains(zero.Warnings, warning => warning.Contains("weight 0"));
    }

    [Fact]
    public void Markdown_keeps_unknowns_notes_sources_dates_and_what_could_change_the_answer()
    {
        var definition = new ComparisonDefinition("Boiler", "Which boiler?",
            [new ComparisonCriterion("price", "Price", 3), new ComparisonCriterion("quiet", "Noise", 1)],
            [
                new ComparisonOption("a", "Alpha | pro", null, new Dictionary<string, ComparisonCell>
                {
                    ["price"] = new(8, "€1,900 incl. fitting", "quote-a.pdf", new DateOnly(2026, 10, 1))
                }),
                new ComparisonOption("b", "Beta", "Cheaper", new Dictionary<string, ComparisonCell>
                {
                    ["price"] = new(5), ["quiet"] = new(9)
                })
            ]);

        var markdown = ComparisonCalculator.ToMarkdown(definition, ComparisonCalculator.Evaluate(definition));

        Assert.Contains("# Boiler", markdown);
        Assert.Contains("Alpha / pro", markdown);
        Assert.Contains("Noise: unknown", markdown);
        Assert.Contains("€1,900 incl. fitting", markdown);
        Assert.Contains("(source: quote-a.pdf)", markdown);
        Assert.Contains("(as of 2026-10-01)", markdown);
        Assert.Contains("never counted as zero", markdown);
    }

    [Fact]
    public void The_validator_trims_limits_and_rejects_bad_shapes()
    {
        var good = new ComparisonDefinition("  Boiler  ", null,
            [new ComparisonCriterion("price", " Price ", 5)],
            [new ComparisonOption("a", " Alpha ", null, new Dictionary<string, ComparisonCell>
            {
                ["price"] = new(7.456, "  note  "), ["ghost"] = new(5)
            })], "a");
        var (normalized, _, _) = ComparisonValidator.Normalize(good);
        Assert.Equal("Boiler", normalized!.Title);
        Assert.Equal("Price", normalized.Criteria[0].Name);
        Assert.Equal(7.46, normalized.Options[0].Cells["price"].Score);
        Assert.Equal("note", normalized.Options[0].Cells["price"].Note);
        Assert.False(normalized.Options[0].Cells.ContainsKey("ghost"));
        Assert.Equal("a", normalized.ChosenOptionId);

        Assert.Equal("title", ComparisonValidator.Normalize(good with { Title = "  " }).Field);
        Assert.Equal("criteria", ComparisonValidator.Normalize(good with
        {
            Criteria = [new ComparisonCriterion("Price!", "Price", 5)]
        }).Field);
        Assert.Equal("criteria", ComparisonValidator.Normalize(good with
        {
            Criteria = [new ComparisonCriterion("price", "Price", 11)]
        }).Field);
        Assert.Equal("options", ComparisonValidator.Normalize(good with
        {
            Options = Enumerable.Range(0, 9).Select(i => new ComparisonOption($"o{i}", $"O{i}", null, new Dictionary<string, ComparisonCell>())).ToArray()
        }).Field);
        Assert.Equal("cells", ComparisonValidator.Normalize(good with
        {
            Options = [new ComparisonOption("a", "Alpha", null, new Dictionary<string, ComparisonCell> { ["price"] = new(11) })]
        }).Field);
        Assert.Null(ComparisonValidator.Normalize(good with { ChosenOptionId = "nope" }).Definition!.ChosenOptionId);
    }
}
