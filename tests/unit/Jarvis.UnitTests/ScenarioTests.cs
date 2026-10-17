using Jarvis.Agents.Scenarios;
using Jarvis.Application.Decisions;
using Jarvis.Application.Library;
using Jarvis.Application.Scenarios;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ScenarioTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fd01");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static SavingsAssumptions Cost(decimal monthly, int months = 24, decimal once = 0, string currency = "EUR") =>
        new(monthly, months, once, currency);

    private static ScenarioRequest Savings(SavingsAssumptions now, SavingsAssumptions next, string? title = "Switch phone plan") =>
        new(ScenarioKinds.Savings, title, now, next, null, null);

    private static ScenarioRequest Time(TimeAssumptions now, TimeAssumptions next) =>
        new(ScenarioKinds.Time, "Finish the tiles", null, null, now, next);

    private static ScenarioResult Run(ScenarioRequest request)
    {
        var result = ScenarioCalculator.Evaluate(request, out var field, out var problem);
        Assert.True(result is not null, $"{field}: {problem}");
        return result;
    }

    [Fact]
    public void Savings_match_exact_decimal_arithmetic_with_the_working_shown()
    {
        var result = Run(Savings(Cost(12.99m), Cost(9.49m, once: 15m)));

        Assert.Equal(311.76m, result.Baseline.Primary);
        Assert.Equal(242.76m, result.Alternative.Primary); // 9.49 × 24 + 15
        var total = result.Deltas.Single(d => d.Label == "Total over the period");
        Assert.Equal(69.00m, total.Value);
        Assert.Contains("(12.99 × 24 + 0.00) − (9.49 × 24 + 15.00) = 69.00", total.Explanation);
        Assert.Equal(3.50m, result.Deltas.Single(d => d.Label == "Per month").Value);
    }

    [Fact]
    public void Break_even_rounds_up_and_says_when_it_never_happens_in_the_period()
    {
        var quick = Run(Savings(Cost(10m), Cost(7m, once: 20m)));
        var slow = Run(Savings(Cost(10m, 3), Cost(9m, 3, once: 20m)));

        Assert.Equal(7m, quick.Deltas.Single(d => d.Label == "Months to break even").Value); // 20 / 3
        Assert.Contains("does not break even within 3 months", Assert.Single(slow.Notes, n => n.Contains("break even")));
    }

    [Fact]
    public void A_dearer_alternative_has_no_saving_and_says_so()
    {
        var result = Run(Savings(Cost(5m), Cost(8m)));

        Assert.True(result.Deltas.Single(d => d.Label == "Total over the period").Value < 0);
        Assert.Contains(result.Notes, n => n.Contains("no saving"));
    }

    [Fact]
    public void Different_currencies_and_months_are_refused_not_added_up()
    {
        Assert.Equal("currency", Fail(Savings(Cost(10m), Cost(9m, currency: "USD"))));
        Assert.Equal("alternative.months", Fail(Savings(Cost(10m, 12), Cost(9m, 24))));
        Assert.Equal("currency", Fail(Savings(Cost(10m, currency: "EURO"), Cost(9m, currency: "EURO"))));
    }

    [Theory]
    [InlineData(-1, 12, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 121, 0)]
    [InlineData(10, 12, -5)]
    [InlineData(2_000_000, 12, 0)]
    public void Impossible_numbers_are_refused_with_the_field(decimal monthly, int months, decimal once)
    {
        var field = Fail(Savings(Cost(monthly, months, once), Cost(1m, months)));

        Assert.StartsWith("baseline.", field);
    }

    private static string? Fail(ScenarioRequest request)
    {
        Assert.Null(ScenarioCalculator.Evaluate(request, out var field, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
        return field;
    }

    [Fact]
    public void Time_scenarios_add_the_buffer_and_explain_the_weeks()
    {
        var result = Run(Time(new TimeAssumptions(5m, 40m, 20m), new TimeAssumptions(8m, 40m, 20m)));

        Assert.Equal(9.6m, result.Baseline.Primary); // 40 × 1.2 / 5
        Assert.Equal(6.0m, result.Alternative.Primary);
        var weeks = result.Deltas.Single(d => d.Label == "Weeks");
        Assert.Equal(3.6m, weeks.Value);
        Assert.Contains("hours × (1 + buffer) ÷ hours per week", weeks.Explanation);
        Assert.Contains(result.Notes, n => n.Contains("does not look at your calendar"));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(169, 10, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(5, 10, 301)]
    [InlineData(5, 10, -1)]
    public void Impossible_time_numbers_are_refused(decimal hours, decimal task, decimal buffer)
    {
        Assert.NotNull(Fail(Time(new TimeAssumptions(hours, task, buffer), new TimeAssumptions(5m, 10m, 0m))));
    }

    [Fact]
    public void An_unknown_kind_or_missing_side_is_refused()
    {
        Assert.Equal("kind", Fail(new ScenarioRequest("magic", null, null, null, null, null)));
        Assert.Equal("savings", Fail(new ScenarioRequest(ScenarioKinds.Savings, null, Cost(1m), null, null, null)));
        Assert.Equal("time", Fail(new ScenarioRequest(ScenarioKinds.Time, null, null, null, null, null)));
    }

    private sealed class Setup
    {
        public readonly List<NoteDraft> Notes = [];
        public readonly List<CreateDecisionRequest> Decisions = [];
        public ScenarioService Service => new(
            Fake<ILibraryService>.Create(("AddNoteAsync", args =>
            {
                Notes.Add((NoteDraft)args[1]!);
                return LibraryOperation<LibraryItem>.Ok(new LibraryItem(Guid.NewGuid(), Owner, "report", null, "t", "",
                    [], [], "c", "app", null, Now, Now));
            })),
            Fake<IDecisionService>.Create(("CreateAsync", args =>
            {
                var request = (CreateDecisionRequest)args[1]!;
                Decisions.Add(request);
                return new DecisionView(Guid.NewGuid(), request.Title, request.Context, request.Prediction,
                    request.Probability, request.ReviewOn, "open", null, null, null, Now);
            })),
            new FixedClock());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Saving_writes_one_library_report_with_both_sides_and_no_decision_unless_asked()
    {
        var s = new Setup();
        var project = Guid.NewGuid();

        var saved = await s.Service.SaveAsync(Owner,
            new ScenarioSaveRequest(Savings(Cost(12.99m), Cost(9.49m, once: 15m)), project, null, null), default);

        Assert.True(saved.Succeeded);
        Assert.Null(saved.Value!.DecisionId);
        var note = Assert.Single(s.Notes);
        Assert.Equal(project, note.ProjectId);
        Assert.Contains("scenario", note.Tags!);
        Assert.Contains("## Now", note.Content);
        Assert.Contains("## Alternative", note.Content);
        Assert.Contains("changed nothing in your budgets", note.Content);
        Assert.Empty(s.Decisions);
    }

    [Fact]
    public async Task A_decision_needs_both_confidence_and_a_future_review_day_and_is_checked_before_saving()
    {
        var s = new Setup();
        var request = Savings(Cost(10m), Cost(8m));

        var half = await s.Service.SaveAsync(Owner, new ScenarioSaveRequest(request, null, 0.7, null), default);
        var past = await s.Service.SaveAsync(Owner,
            new ScenarioSaveRequest(request, null, 0.7, new DateOnly(2026, 10, 1)), default);

        Assert.Equal("decision", half.Field);
        Assert.Equal("decisionReviewOn", past.Field);
        Assert.Empty(s.Notes);
        Assert.Empty(s.Decisions);

        var ok = await s.Service.SaveAsync(Owner,
            new ScenarioSaveRequest(request, null, 0.7, new DateOnly(2027, 1, 15)), default);
        Assert.NotNull(ok.Value!.DecisionId);
        Assert.Single(s.Notes);
        Assert.Contains("Go with the alternative", Assert.Single(s.Decisions).Title);
    }

    [Fact]
    public async Task An_invalid_scenario_saves_nothing()
    {
        var s = new Setup();

        var result = await s.Service.SaveAsync(Owner,
            new ScenarioSaveRequest(Savings(Cost(10m), Cost(9m, currency: "USD")), null, null, null), default);

        Assert.Equal("currency", result.Field);
        Assert.Empty(s.Notes);
    }

    [Fact]
    public async Task The_tools_report_the_arithmetic_and_refuse_mixed_up_input()
    {
        var tools = new ScenarioAgentTools(new Setup().Service);

        var cost = await tools.CompareCostScenarioAsync(12.99m, 9.49m, 24, 15m);
        var time = await tools.CompareTimeScenarioAsync(40m, 5m, 8m);
        var bad = await tools.CompareCostScenarioAsync(10m, 9m, 0);

        Assert.Contains("= 69.00", cost);
        Assert.Contains("Nothing here changes a budget", cost);
        Assert.Contains("9.6 weeks", time);
        Assert.Contains("I could not work that out", bad);
    }
}
