using Jarvis.Application.Conversations;
using Jarvis.Application.Scenarios;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// What-if scenarios. Evaluating is stateless arithmetic on the numbers sent; saving writes only a Library
/// report and, when asked, one decision. Nothing here reads or changes budgets, accounts or the calendar.
/// </summary>
internal static class ScenarioEndpoints
{
    public static RouteGroupBuilder MapScenarioEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/scenarios").WithTags("Scenarios");

        group.MapPost("/evaluate", (ScenarioRequest request, IScenarioService scenarios) =>
        {
            var result = scenarios.Evaluate(request);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : EndpointHelpers.Invalid(result.Field ?? "scenario", result.Message ?? "The scenario is invalid.");
        }).WithName("EvaluateScenario");

        group.MapPost("/save", async (ScenarioSaveRequest request, IScenarioService scenarios, ICurrentUser user,
            CancellationToken ct) =>
        {
            if (request.Scenario is null) return EndpointHelpers.Invalid("scenario", "Describe the scenario.");
            var saved = await scenarios.SaveAsync(user.OwnerId, request, ct);
            return saved.Succeeded
                ? Results.Ok(saved.Value)
                : EndpointHelpers.Invalid(saved.Field ?? "scenario", saved.Message ?? "The scenario is invalid.");
        }).WithName("SaveScenario");

        return api;
    }
}
