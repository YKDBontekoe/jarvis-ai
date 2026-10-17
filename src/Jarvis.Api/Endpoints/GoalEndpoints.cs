using Jarvis.Application.Conversations;
using Jarvis.Application.Goals;

namespace Jarvis.Api.Endpoints;

public sealed record GoalRequest(
    string? Title,
    string? Motivation,
    DateOnly? Horizon,
    string? Target,
    int? CheckInEveryDays,
    int? WeeklyMinutes,
    string? Status,
    Guid? ProjectId,
    IReadOnlyList<Guid>? HabitIds,
    IReadOnlyList<Guid>? CommitmentIds,
    bool? ClearProject,
    bool? ClearHorizon);

public sealed record GoalCheckInRequest(string? Progress, string? Friction, string? Adjustment, string? Feeling);

public sealed record GoalExperimentRequest(string? Change, string? ExpectedBenefit, int? DurationDays);

public sealed record GoalExperimentNoteRequest(string? Text);

public sealed record GoalExperimentFinishRequest(bool? Keep, string? Conclusion);

public sealed record GoalPlanActionRequest(string? Action, int? Minutes);

/// <summary>
/// Goals: an outcome the owner chose, the habits, project and promises that serve it, a small weekly plan the
/// owner accepts or declines, check-ins in their own words, and bounded experiments. Suggestions are proposals;
/// nothing is scheduled or changed elsewhere until the owner says so.
/// </summary>
internal static class GoalEndpoints
{
    public static RouteGroupBuilder MapGoalEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goals").WithTags("Goals");

        group.MapGet("", async (IGoalService goals, ICurrentUser user, bool? includeArchived, CancellationToken ct) =>
            Results.Ok(await goals.ListAsync(user.OwnerId, includeArchived ?? false, ct))).WithName("ListGoals");

        group.MapPost("", async (GoalRequest request, IGoalService goals, ICurrentUser user, CancellationToken ct) =>
        {
            var created = await goals.CreateAsync(user.OwnerId, ToInput(request), ct);
            return created.Succeeded
                ? Results.Created($"/api/v1/goals/{created.Value!.Goal.Id}", created.Value)
                : Failure(created);
        }).WithName("CreateGoal");

        group.MapGet("/{id:guid}", async (Guid id, IGoalService goals, ICurrentUser user, CancellationToken ct) =>
            await goals.GetAsync(user.OwnerId, id, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .WithName("GetGoal");

        group.MapPatch("/{id:guid}", async (Guid id, GoalRequest request, IGoalService goals, ICurrentUser user,
            CancellationToken ct) => Done(await goals.UpdateAsync(user.OwnerId, id, ToInput(request), ct)))
            .WithName("UpdateGoal");

        group.MapDelete("/{id:guid}", async (Guid id, IGoalService goals, ICurrentUser user, CancellationToken ct) =>
            await goals.DeleteAsync(user.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteGoal");

        group.MapPost("/{id:guid}/reset-season", async (Guid id, IGoalService goals, ICurrentUser user,
            CancellationToken ct) => Done(await goals.ResetSeasonAsync(user.OwnerId, id, ct)))
            .WithName("ResetGoalSeason");

        group.MapPost("/{id:guid}/check-ins", async (Guid id, GoalCheckInRequest request, IGoalService goals,
            ICurrentUser user, CancellationToken ct) =>
            Done(await goals.AddCheckInAsync(user.OwnerId, id,
                new CheckInInput(request.Progress, request.Friction, request.Adjustment, request.Feeling), ct)))
            .WithName("AddGoalCheckIn");

        group.MapPost("/{id:guid}/plan", async (Guid id, IGoalService goals, ICurrentUser user,
            CancellationToken ct) => Done(await goals.ProposePlanAsync(user.OwnerId, id, ct)))
            .WithName("ProposeGoalPlan");

        group.MapPost("/{id:guid}/plan/items/{itemId:guid}", async (Guid id, Guid itemId, GoalPlanActionRequest request,
            IGoalService goals, ICurrentUser user, string? timeZone, CancellationToken ct) =>
            Done(await goals.ActOnPlanItemAsync(user.OwnerId, id, itemId,
                new PlanItemAction(request.Action ?? string.Empty, request.Minutes), timeZone, ct)))
            .WithName("ActOnGoalPlanItem");

        group.MapPost("/{id:guid}/experiments", async (Guid id, GoalExperimentRequest request, IGoalService goals,
            ICurrentUser user, CancellationToken ct) =>
            Done(await goals.StartExperimentAsync(user.OwnerId, id,
                new ExperimentInput(request.Change, request.ExpectedBenefit, request.DurationDays), ct)))
            .WithName("StartGoalExperiment");

        group.MapPost("/{id:guid}/experiments/{experimentId:guid}/notes", async (Guid id, Guid experimentId,
            GoalExperimentNoteRequest request, IGoalService goals, ICurrentUser user, CancellationToken ct) =>
            Done(await goals.NoteExperimentAsync(user.OwnerId, id, experimentId, request.Text, ct)))
            .WithName("NoteGoalExperiment");

        group.MapPost("/{id:guid}/experiments/{experimentId:guid}/finish", async (Guid id, Guid experimentId,
            GoalExperimentFinishRequest request, IGoalService goals, ICurrentUser user, CancellationToken ct) =>
        {
            if (request.Keep is null) return EndpointHelpers.Invalid("keep", "Say whether to keep the change.");
            return Done(await goals.FinishExperimentAsync(user.OwnerId, id, experimentId, request.Keep.Value,
                request.Conclusion, ct));
        }).WithName("FinishGoalExperiment");

        group.MapPost("/{id:guid}/review/dismiss", async (Guid id, IGoalService goals, ICurrentUser user,
            CancellationToken ct) => Done(await goals.DismissReviewAsync(user.OwnerId, id, ct)))
            .WithName("DismissGoalReview");

        return api;
    }

    private static GoalInput ToInput(GoalRequest request) => new(request.Title, request.Motivation, request.Horizon,
        request.Target, request.CheckInEveryDays, request.WeeklyMinutes, request.Status, request.ProjectId,
        request.HabitIds, request.CommitmentIds, request.ClearProject ?? false, request.ClearHorizon ?? false);

    private static IResult Done(GoalOperation<GoalView> operation) =>
        operation.Succeeded ? Results.Ok(operation.Value) : Failure(operation);

    private static IResult Failure<T>(GoalOperation<T> operation) => operation.Failure switch
    {
        GoalFailure.NotFound => Results.NotFound(),
        GoalFailure.Conflict => Results.Conflict(new { message = operation.Message }),
        _ => EndpointHelpers.Invalid(operation.Field ?? "goal", operation.Message ?? "The goal is invalid.")
    };
}
