using System.ComponentModel;
using System.Text;
using Jarvis.Application.Scenarios;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Scenarios;

internal sealed class ScenarioToolContributor(IScenarioService scenarios) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new ScenarioAgentTools(scenarios);
        yield return AIFunctionFactory.Create(tools.CompareCostScenarioAsync);
        yield return AIFunctionFactory.Create(tools.CompareTimeScenarioAsync);
    }
}

/// <summary>
/// What-if arithmetic with numbers the user gave. Nothing is stored or changed; the app does the calculation, so
/// the model reports it and never invents a figure.
/// </summary>
internal sealed class ScenarioAgentTools(IScenarioService scenarios)
{
    [Description("Compare what a recurring cost adds up to now and with an alternative over the same number of months, including a one-time switching cost. Use only numbers the user gave you; ask when one is missing. It does not change any budget or subscription and saves nothing; the user can save the scenario in the app.")]
    public Task<string> CompareCostScenarioAsync(
        [Description("What it costs per month now.")] decimal currentMonthlyCost,
        [Description("What the alternative costs per month.")] decimal alternativeMonthlyCost,
        [Description("How many months to compare, 1 to 120.")] int months,
        [Description("A one-time cost of switching, 0 if none.")] decimal switchingCost = 0,
        [Description("Three-letter currency, the same for both sides.")] string currency = "EUR",
        CancellationToken cancellationToken = default)
    {
        var result = scenarios.Evaluate(new ScenarioRequest(ScenarioKinds.Savings, null,
            new SavingsAssumptions(currentMonthlyCost, months, 0, currency),
            new SavingsAssumptions(alternativeMonthlyCost, months, switchingCost, currency), null, null));
        return Task.FromResult(Describe(result));
    }

    [Description("Work out how many weeks a piece of work takes at a weekly pace, now and with a different pace, including a buffer for the unexpected. Use only numbers the user gave you. It does not look at or change the calendar.")]
    public Task<string> CompareTimeScenarioAsync(
        [Description("Hours of work in total.")] decimal taskHours,
        [Description("Hours per week the user can give now.")] decimal currentHoursPerWeek,
        [Description("Hours per week in the alternative.")] decimal alternativeHoursPerWeek,
        [Description("Extra time for the unexpected, as a percentage.")] decimal bufferPercent = 20,
        CancellationToken cancellationToken = default)
    {
        var result = scenarios.Evaluate(new ScenarioRequest(ScenarioKinds.Time, null, null, null,
            new TimeAssumptions(currentHoursPerWeek, taskHours, bufferPercent),
            new TimeAssumptions(alternativeHoursPerWeek, taskHours, bufferPercent)));
        return Task.FromResult(Describe(result));
    }

    private static string Describe(ScenarioOperation<ScenarioResult> operation)
    {
        if (!operation.Succeeded) return $"I could not work that out: {operation.Message}";
        var result = operation.Value!;
        var text = new StringBuilder();
        text.Append("Now: ").AppendLine(result.Baseline.Summary);
        text.Append("Alternative: ").AppendLine(result.Alternative.Summary);
        foreach (var delta in result.Deltas) text.Append("- ").Append(delta.Label).Append(": ").AppendLine(delta.Explanation);
        foreach (var note in result.Notes) text.Append("Note: ").AppendLine(note);
        return text.ToString();
    }
}
