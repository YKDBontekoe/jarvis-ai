using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Habits;

namespace Jarvis.Api.Endpoints;

/// <summary>The owner's habits and check-ins. Audit entries carry ids only, never habit names.</summary>
internal static class HabitEndpoints
{
    public static RouteGroupBuilder MapHabitEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var habits = api.MapGroup("/habits");

        habits.MapGet("", async (IHabitService service, ICurrentUser currentUser, bool? includeArchived,
            CancellationToken ct) =>
        {
            var ownerId = currentUser.OwnerId;
            var all = await service.ListAsync(ownerId, includeArchived ?? false, ct);
            return Results.Ok(new HabitsOverviewDto(await service.TodayAsync(ownerId, ct),
                all.Select(x => x.ToDto()).ToArray(), await service.GetSettingsAsync(ownerId, ct)));
        }).WithName("ListHabits");

        habits.MapGet("/{id:guid}", async (Guid id, IHabitService service, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var habit = await service.GetAsync(id, currentUser.OwnerId, ct);
            return habit is null ? Results.NotFound() : Results.Ok(habit.ToDto());
        }).WithName("GetHabit");

        habits.MapPost("", async (HabitRequest request, IHabitService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(currentUser.OwnerId, request.ToDraft(), request.TimeZoneId, ct);
            if (Failed(result) is { } failure) return failure;
            var habit = result.Value!;
            await AuditAsync(audit, logger, currentUser, "habit.created", habit.Habit.Id, ct);
            return Results.Created($"/api/v1/habits/{habit.Habit.Id}", habit.ToDto());
        }).WithName("CreateHabit");

        habits.MapPut("/{id:guid}", async (Guid id, HabitRequest request, IHabitService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, currentUser.OwnerId, request.ToDraft(), ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "habit.updated", id, ct);
            return Results.Ok(result.Value!.ToDto());
        }).WithName("UpdateHabit");

        habits.MapPut("/{id:guid}/archived", async (Guid id, HabitArchiveRequest request, IHabitService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.SetArchivedAsync(id, currentUser.OwnerId, request.Archived, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, request.Archived ? "habit.archived" : "habit.restored", id,
                ct);
            return Results.Ok(result.Value!.ToDto());
        }).WithName("ArchiveHabit");

        habits.MapDelete("/{id:guid}", async (Guid id, IHabitService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await service.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "habit.deleted", id, ct, risk: "moderate");
            return Results.NoContent();
        }).WithName("DeleteHabit");

        habits.MapPost("/{id:guid}/check-ins", async (Guid id, HabitCheckInRequest request, IHabitService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var done = request.Done ?? true;
            var result = await service.SetDoneAsync(id, currentUser.OwnerId, request.Date, done, HabitSources.App,
                ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, done ? "habit.checked_in" : "habit.check_in_undone", id, ct,
                date: request.Date);
            return Results.Ok(result.Value!.ToDto());
        }).WithName("CheckInHabit");

        habits.MapGet("/settings", async (IHabitService service, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await service.GetSettingsAsync(currentUser.OwnerId, ct)))
            .WithName("GetHabitSettings");

        habits.MapPut("/settings", async (HabitSettings request, IHabitService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            HabitSettings saved;
            try { saved = await service.SaveSettingsAsync(currentUser.OwnerId, request, ct); }
            catch (ArgumentException exception)
            {
                return ApiProblemResults.Validation(
                    exception.ParamName == nameof(HabitSettings.TimeZoneId) ? "timeZoneId" : "checkInTime",
                    exception.Message);
            }
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "habits",
                "habit.settings_updated", "low", true, null,
                JsonSerializer.Serialize(new { eveningCheckIn = saved.EveningCheckIn, source = "app" }), ct);
            return Results.Ok(saved);
        }).WithName("SaveHabitSettings");

        return api;
    }

    private static HabitDraft ToDraft(this HabitRequest request) =>
        new(request.Name, request.Icon, request.Cadence, request.TargetPerWeek);

    private static IResult? Failed<T>(HabitOperation<T> result) => result.Failure switch
    {
        HabitFailure.None => null,
        HabitFailure.NotFound => Results.NotFound(),
        HabitFailure.Conflict => ApiProblemResults.Conflict(result.Message ?? "That habit already exists."),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid habitId, CancellationToken ct, string risk = "low", DateOnly? date = null) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "habits", action, risk, true, null,
            JsonSerializer.Serialize(new { resourceId = habitId, date, source = "app" }), ct);
}
