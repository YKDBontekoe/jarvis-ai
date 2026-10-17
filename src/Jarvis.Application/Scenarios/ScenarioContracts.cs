using System.Globalization;
using System.Text;
using Jarvis.Application.Decisions;
using Jarvis.Application.Library;

namespace Jarvis.Application.Scenarios;

public static class ScenarioKinds
{
    public const string Savings = "savings";
    public const string Time = "time";
}

/// <summary>
/// What a recurring cost looks like over a horizon. <see cref="OneTimeCost"/> is what it takes to switch (a fee,
/// a setup); <see cref="Months"/> is how long the owner will keep it.
/// </summary>
public sealed record SavingsAssumptions(decimal MonthlyCost, int Months, decimal OneTimeCost, string Currency);

/// <summary>
/// How long a piece of work takes at a weekly pace. <see cref="BufferPercent"/> is the slack added for the
/// unexpected, so the answer is not a best case.
/// </summary>
public sealed record TimeAssumptions(decimal HoursPerWeek, decimal TaskHours, decimal BufferPercent);

public sealed record ScenarioRequest(
    string? Kind,
    string? Title,
    SavingsAssumptions? SavingsBaseline,
    SavingsAssumptions? SavingsAlternative,
    TimeAssumptions? TimeBaseline,
    TimeAssumptions? TimeAlternative);

/// <summary>One way the two sides differ, with the arithmetic in words.</summary>
public sealed record ScenarioDelta(string Label, decimal Value, string Unit, string Explanation);

public sealed record ScenarioOutcome(string Label, decimal Primary, string Unit, string Summary);

public sealed record ScenarioResult(
    string Kind,
    ScenarioOutcome Baseline,
    ScenarioOutcome Alternative,
    IReadOnlyList<ScenarioDelta> Deltas,
    IReadOnlyList<string> Notes);

public sealed record ScenarioSaved(Guid LibraryItemId, Guid? DecisionId);

public sealed record ScenarioSaveRequest(ScenarioRequest Scenario, Guid? ProjectId, double? DecisionProbability,
    DateOnly? DecisionReviewOn);

public static class ScenarioRules
{
    public const decimal MaxMoney = 1_000_000m;
    public const int MaxMonths = 120;
    public const decimal MaxHoursPerWeek = 168m;
    public const decimal MaxTaskHours = 100_000m;
    public const decimal MaxBuffer = 300m;
}

/// <summary>
/// Pure arithmetic for the two scenarios. Everything is decimal, so a saved scenario reproduces exactly; a
/// mixed-currency pair is refused rather than added up.
/// </summary>
public static class ScenarioCalculator
{
    public static ScenarioResult? Evaluate(ScenarioRequest request, out string? field, out string? problem)
    {
        field = problem = null;
        return request.Kind switch
        {
            ScenarioKinds.Savings => Savings(request, out field, out problem),
            ScenarioKinds.Time => Time(request, out field, out problem),
            _ => Fail("kind", "Choose savings or time.", out field, out problem)
        };
    }

    private static ScenarioResult? Fail(string name, string message, out string? field, out string? problem)
    {
        field = name;
        problem = message;
        return null;
    }

    private static ScenarioResult? Savings(ScenarioRequest request, out string? field, out string? problem)
    {
        field = problem = null;
        if (request.SavingsBaseline is not { } now || request.SavingsAlternative is not { } next)
            return Fail("savings", "Give both the current and the alternative assumptions.", out field, out problem);
        var currencyNow = now.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        var currencyNext = next.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (currencyNow.Length != 3 || currencyNext.Length != 3 || !currencyNow.All(char.IsLetter) ||
            !currencyNext.All(char.IsLetter))
            return Fail("currency", "Use a three-letter currency such as EUR.", out field, out problem);
        if (currencyNow != currencyNext)
            return Fail("currency",
                $"The two sides use different currencies ({currencyNow} and {currencyNext}). Convert one first; Jarvis will not add them up.",
                out field, out problem);
        foreach (var (name, side) in new[] { ("baseline", now), ("alternative", next) })
        {
            if (side.MonthlyCost is < 0 or > ScenarioRules.MaxMoney)
                return Fail($"{name}.monthlyCost", "A monthly cost must be between 0 and 1,000,000.", out field, out problem);
            if (side.OneTimeCost is < 0 or > ScenarioRules.MaxMoney)
                return Fail($"{name}.oneTimeCost", "A one-time cost must be between 0 and 1,000,000.", out field, out problem);
            if (side.Months is < 1 or > ScenarioRules.MaxMonths)
                return Fail($"{name}.months", $"Use 1 to {ScenarioRules.MaxMonths} months.", out field, out problem);
        }
        if (now.Months != next.Months)
            return Fail("alternative.months", "Compare both over the same number of months.", out field, out problem);

        var totalNow = now.MonthlyCost * now.Months + now.OneTimeCost;
        var totalNext = next.MonthlyCost * next.Months + next.OneTimeCost;
        var monthlyDelta = now.MonthlyCost - next.MonthlyCost;
        var deltas = new List<ScenarioDelta>
        {
            new("Total over the period", Math.Round(totalNow - totalNext, 2), currencyNow,
                $"({Money(now.MonthlyCost)} × {now.Months} + {Money(now.OneTimeCost)}) − ({Money(next.MonthlyCost)} × {next.Months} + {Money(next.OneTimeCost)}) = {Money(totalNow - totalNext)}"),
            new("Per month", Math.Round(monthlyDelta, 2), currencyNow,
                $"{Money(now.MonthlyCost)} − {Money(next.MonthlyCost)} = {Money(monthlyDelta)} a month")
        };
        var notes = new List<string>();
        var extraOneTime = next.OneTimeCost - now.OneTimeCost;
        if (monthlyDelta > 0 && extraOneTime > 0)
        {
            var months = Math.Ceiling(extraOneTime / monthlyDelta);
            deltas.Add(new ScenarioDelta("Months to break even", months, "months",
                $"{Money(extraOneTime)} extra up front ÷ {Money(monthlyDelta)} saved a month, rounded up"));
            if (months > now.Months)
                notes.Add($"It does not break even within {now.Months} months.");
        }
        else if (monthlyDelta <= 0)
            notes.Add("The alternative does not cost less each month, so there is no saving to break even on.");
        notes.Add("Assumes the prices stay as entered. Nothing here changes a budget, account or subscription.");

        return new ScenarioResult(ScenarioKinds.Savings,
            new ScenarioOutcome("Now", Math.Round(totalNow, 2), currencyNow,
                $"{Money(now.MonthlyCost)} {currencyNow} a month for {now.Months} months, {Money(totalNow)} in total."),
            new ScenarioOutcome("Alternative", Math.Round(totalNext, 2), currencyNow,
                $"{Money(next.MonthlyCost)} {currencyNow} a month for {next.Months} months plus {Money(next.OneTimeCost)} once, {Money(totalNext)} in total."),
            deltas, notes);
    }

    private static ScenarioResult? Time(ScenarioRequest request, out string? field, out string? problem)
    {
        field = problem = null;
        if (request.TimeBaseline is not { } now || request.TimeAlternative is not { } next)
            return Fail("time", "Give both the current and the alternative assumptions.", out field, out problem);
        foreach (var (name, side) in new[] { ("baseline", now), ("alternative", next) })
        {
            if (side.HoursPerWeek is <= 0 or > ScenarioRules.MaxHoursPerWeek)
                return Fail($"{name}.hoursPerWeek", "Hours per week must be more than 0 and at most 168.", out field, out problem);
            if (side.TaskHours is <= 0 or > ScenarioRules.MaxTaskHours)
                return Fail($"{name}.taskHours", "The work must be more than 0 hours.", out field, out problem);
            if (side.BufferPercent is < 0 or > ScenarioRules.MaxBuffer)
                return Fail($"{name}.bufferPercent", "The buffer is a percentage from 0 to 300.", out field, out problem);
        }
        var weeksNow = Weeks(now);
        var weeksNext = Weeks(next);
        var deltas = new List<ScenarioDelta>
        {
            new("Weeks", Math.Round(weeksNow - weeksNext, 1), "weeks",
                $"{Weeks1(weeksNow)} − {Weeks1(weeksNext)} = {Weeks1(weeksNow - weeksNext)} weeks " +
                $"(hours × (1 + buffer) ÷ hours per week)"),
            new("Hours including buffer", Math.Round(WithBuffer(now) - WithBuffer(next), 1), "hours",
                $"{Hours(WithBuffer(now))} − {Hours(WithBuffer(next))} hours")
        };
        return new ScenarioResult(ScenarioKinds.Time,
            new ScenarioOutcome("Now", Math.Round(weeksNow, 1), "weeks",
                $"{Hours(now.TaskHours)} hours of work at {Hours(now.HoursPerWeek)} hours a week with {now.BufferPercent:0.#}% buffer: {Weeks1(weeksNow)} weeks."),
            new ScenarioOutcome("Alternative", Math.Round(weeksNext, 1), "weeks",
                $"{Hours(next.TaskHours)} hours of work at {Hours(next.HoursPerWeek)} hours a week with {next.BufferPercent:0.#}% buffer: {Weeks1(weeksNext)} weeks."),
            deltas, ["Counts only the hours you say you can give; it does not look at your calendar."]);
    }

    private static decimal WithBuffer(TimeAssumptions t) => t.TaskHours * (1 + t.BufferPercent / 100m);
    private static decimal Weeks(TimeAssumptions t) => WithBuffer(t) / t.HoursPerWeek;

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Hours(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Weeks1(decimal value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    public static string ToMarkdown(string title, ScenarioResult result)
    {
        var text = new StringBuilder();
        text.Append("# ").AppendLine(title);
        text.AppendLine().AppendLine("A what-if. It changed nothing in your budgets, accounts or calendar.");
        text.AppendLine().AppendLine("## Now").AppendLine(result.Baseline.Summary);
        text.AppendLine().AppendLine("## Alternative").AppendLine(result.Alternative.Summary);
        text.AppendLine().AppendLine("## Difference");
        foreach (var delta in result.Deltas) text.Append("- **").Append(delta.Label).Append(":** ").AppendLine(delta.Explanation);
        text.AppendLine().AppendLine("## Assumptions and limits");
        foreach (var note in result.Notes) text.Append("- ").AppendLine(note);
        return text.ToString().TrimEnd();
    }
}

public enum ScenarioFailure
{
    None,
    Invalid
}

public sealed record ScenarioOperation<T>(T? Value, ScenarioFailure Failure = ScenarioFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == ScenarioFailure.None;
    public static ScenarioOperation<T> Ok(T value) => new(value);
    public static ScenarioOperation<T> Invalid(string field, string message) =>
        new(default, ScenarioFailure.Invalid, field, message);
}

public interface IScenarioService
{
    /// <summary>Stateless: nothing is stored and nothing live is read or changed.</summary>
    ScenarioOperation<ScenarioResult> Evaluate(ScenarioRequest request);

    /// <summary>Saves baseline and alternative as a Library report, and optionally logs one decision.</summary>
    Task<ScenarioOperation<ScenarioSaved>> SaveAsync(Guid ownerId, ScenarioSaveRequest request,
        CancellationToken cancellationToken);
}

public sealed class ScenarioService(ILibraryService library, IDecisionService decisions, TimeProvider? timeProvider = null)
    : IScenarioService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public ScenarioOperation<ScenarioResult> Evaluate(ScenarioRequest request)
    {
        var result = ScenarioCalculator.Evaluate(request, out var field, out var problem);
        return result is null
            ? ScenarioOperation<ScenarioResult>.Invalid(field ?? "scenario", problem ?? "The scenario is invalid.")
            : ScenarioOperation<ScenarioResult>.Ok(result);
    }

    public async Task<ScenarioOperation<ScenarioSaved>> SaveAsync(Guid ownerId, ScenarioSaveRequest request,
        CancellationToken cancellationToken)
    {
        var evaluated = Evaluate(request.Scenario);
        if (!evaluated.Succeeded)
            return ScenarioOperation<ScenarioSaved>.Invalid(evaluated.Field!, evaluated.Message!);
        var title = string.Join(' ', (request.Scenario.Title ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        if (title.Length == 0) title = request.Scenario.Kind == ScenarioKinds.Savings ? "Savings scenario" : "Time scenario";
        if (title.Length > 120) title = title[..120];

        // Check the decision input first so a refused decision does not leave a half-saved scenario behind.
        CreateDecisionRequest? decision = null;
        if (request.DecisionProbability is not null || request.DecisionReviewOn is not null)
        {
            if (request.DecisionProbability is not { } probability || request.DecisionReviewOn is not { } reviewOn)
                return ScenarioOperation<ScenarioSaved>.Invalid("decision",
                    "To log a decision, say how sure you are and the day to check how it went.");
            if (reviewOn < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return ScenarioOperation<ScenarioSaved>.Invalid("decisionReviewOn", "Pick a review day that has not passed.");
            decision = new CreateDecisionRequest($"Go with the alternative: {title}",
                $"The alternative turns out better: {evaluated.Value!.Alternative.Summary}", probability, reviewOn,
                $"From the scenario “{title}”. {string.Join(' ', evaluated.Value.Deltas.Select(d => d.Explanation))}");
        }

        var saved = await library.AddNoteAsync(ownerId,
            new NoteDraft(title, ScenarioCalculator.ToMarkdown(title, evaluated.Value!), LibraryKinds.Report, null,
                ["scenario"], LibraryOrigins.App, request.ProjectId), cancellationToken);
        if (!saved.Succeeded)
            return ScenarioOperation<ScenarioSaved>.Invalid(saved.Field ?? "library",
                saved.Message ?? "Could not save to the Library.");
        Guid? decisionId = null;
        if (decision is not null)
        {
            try
            {
                decisionId = (await decisions.CreateAsync(ownerId, decision, cancellationToken)).Id;
            }
            catch (ArgumentException exception)
            {
                return ScenarioOperation<ScenarioSaved>.Invalid(exception.ParamName ?? "decision", exception.Message);
            }
        }
        return ScenarioOperation<ScenarioSaved>.Ok(new ScenarioSaved(saved.Value!.Id, decisionId));
    }
}
