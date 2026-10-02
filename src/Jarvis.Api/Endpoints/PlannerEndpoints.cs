using Jarvis.Application.Conversations;
using Jarvis.Application.Planner;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// The Today timeline and day plan. <c>timeZone</c> is the device's IANA zone; without it the owner's saved zone
/// is used. Nothing here writes to the calendar.
/// </summary>
internal static class PlannerEndpoints
{
    public static RouteGroupBuilder MapPlannerEndpoints(this RouteGroupBuilder api)
    {
        var planner = api.MapGroup("/planner/today");

        planner.MapGet("", async (IDayPlannerService service, ICurrentUser currentUser, string? timeZone,
                CancellationToken ct) =>
            Results.Ok(await service.GetTodayAsync(currentUser.OwnerId, timeZone, ct)))
            .WithName("GetDayPlan");

        planner.MapPost("/items", async (AddDayPlanItemRequest request, IDayPlannerService service,
            ICurrentUser currentUser, string? timeZone, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.AddItemAsync(currentUser.OwnerId, request, timeZone, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("item", exception.Message);
            }
        }).WithName("AddDayPlanItem");

        planner.MapPatch("/items/{id:guid}", async (Guid id, UpdateDayPlanItemRequest request,
            IDayPlannerService service, ICurrentUser currentUser, string? timeZone, CancellationToken ct) =>
        {
            try
            {
                var item = await service.UpdateItemAsync(currentUser.OwnerId, id, request, timeZone, ct);
                return item is null ? Results.NotFound() : Results.Ok(item);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("item", exception.Message);
            }
        }).WithName("UpdateDayPlanItem");

        planner.MapDelete("/items/{id:guid}", async (Guid id, IDayPlannerService service, ICurrentUser currentUser,
                string? timeZone, CancellationToken ct) =>
            await service.RemoveItemAsync(currentUser.OwnerId, id, timeZone, ct)
                ? Results.NoContent()
                : Results.NotFound())
            .WithName("RemoveDayPlanItem");

        planner.MapPost("/plan", async (IDayPlannerService service, ICurrentUser currentUser, string? timeZone,
                CancellationToken ct) =>
            Results.Ok(await service.PlanAsync(currentUser.OwnerId, timeZone, ct)))
            .WithName("PlanMyDay");

        planner.MapDelete("/plan", async (IDayPlannerService service, ICurrentUser currentUser, string? timeZone,
                CancellationToken ct) =>
            Results.Ok(await service.ClearPlanAsync(currentUser.OwnerId, timeZone, ct)))
            .WithName("ClearDayPlan");

        planner.MapPut("/hours", async (SaveDayHoursRequest request, IDayPlannerService service,
            ICurrentUser currentUser, string? timeZone, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SaveHoursAsync(currentUser.OwnerId, request, timeZone, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("hours", exception.Message);
            }
        }).WithName("SaveDayHours");

        return api;
    }
}
