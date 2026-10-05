using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Routines;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// Routines Jarvis noticed in the owner's life, each with a ready automation. Accepting one only creates a
/// draft; the owner still reviews and switches it on in the automation screen.
/// </summary>
internal static class RoutineEndpoints
{
    public static RouteGroupBuilder MapRoutineEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/routines").WithTags("Routines");

        group.MapGet("/suggestions", async (IRoutineSuggestionService routines, ICurrentUser user,
            CancellationToken ct) =>
            Results.Ok(await routines.ListAsync(user.OwnerId, ct))).WithName("ListRoutineSuggestions");

        group.MapPost("/suggestions/refresh", async (IRoutineSuggestionService routines, ICurrentUser user,
            CancellationToken ct) =>
            Results.Ok(await routines.RefreshAsync(user.OwnerId, true, ct))).WithName("RefreshRoutineSuggestions");

        group.MapPost("/suggestions/{id:guid}/accept", async (Guid id, IRoutineSuggestionService routines,
            IAuditEventStore audit, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await routines.AcceptAsync(id, user.OwnerId, ct);
            if (result is null) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, user.OwnerId, "routines", "routine.accepted",
                "low", true, null, JsonSerializer.Serialize(new { suggestionId = id, automationId = result.AutomationId }),
                ct);
            return Results.Ok(new { automationId = result.AutomationId, status = result.Suggestion.Status });
        }).WithName("AcceptRoutineSuggestion");

        group.MapPost("/suggestions/{id:guid}/dismiss", async (Guid id, IRoutineSuggestionService routines,
            ICurrentUser user, CancellationToken ct) =>
            await routines.DismissAsync(id, user.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DismissRoutineSuggestion");

        return api;
    }
}
