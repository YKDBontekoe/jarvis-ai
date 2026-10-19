using Jarvis.Application.Conversations;
using Jarvis.Application.HomeRoutines;

namespace Jarvis.Api.Endpoints;

public sealed record RoutineChoiceRequest(List<string>? Removed, Dictionary<string, string>? Devices);

/// <summary>
/// Home routines: templates, the devices a source reports (an example fixture for now), a plan per routine the owner
/// can edit, and simulated runs with honest per-action outcomes. Nothing here reaches a real device.
/// </summary>
internal static class HomeRoutineEndpoints
{
    public static RouteGroupBuilder MapHomeRoutineEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/home-routines").WithTags("HomeRoutines");

        group.MapGet("", async (IHomeRoutineService routines, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await routines.OverviewAsync(user.OwnerId, ct))).WithName("GetHomeRoutines");

        group.MapGet("/runs", async (IHomeRoutineService routines, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await routines.RunsAsync(user.OwnerId, ct))).WithName("ListHomeRoutineRuns");

        group.MapGet("/{id}/plan", async (string id, IHomeRoutineService routines, ICurrentUser user, CancellationToken ct) =>
            Respond(await routines.PlanAsync(user.OwnerId, id, ct))).WithName("GetHomeRoutinePlan");

        group.MapPut("/{id}/choices", async (string id, RoutineChoiceRequest request, IHomeRoutineService routines,
            ICurrentUser user, CancellationToken ct) =>
            Respond(await routines.ChooseAsync(user.OwnerId, id, request.Removed, request.Devices, ct)))
            .WithName("ChooseHomeRoutine");

        group.MapPost("/{id}/run", async (string id, IHomeRoutineService routines, ICurrentUser user,
            CancellationToken ct) => Respond(await routines.RunAsync(user.OwnerId, id, ct))).WithName("RunHomeRoutine");

        group.MapPost("/runs/{runId}/reverse", async (string runId, IHomeRoutineService routines, ICurrentUser user,
            CancellationToken ct) => Respond(await routines.ReverseAsync(user.OwnerId, runId, ct)))
            .WithName("ReverseHomeRoutineRun");

        return api;
    }

    private static IResult Respond<T>(HomeRoutineOperation<T> result) => result switch
    {
        { NotFound: true } => Results.NotFound(),
        { Succeeded: false } => EndpointHelpers.Invalid(result.Field ?? "request", result.Message ?? "The request is invalid."),
        _ => Results.Ok(result.Value)
    };
}
