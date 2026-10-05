using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Decisions;

namespace Jarvis.Api.Endpoints;

public sealed record CreateDecisionBody(string? Title, string? Prediction, double? Probability, DateOnly? ReviewOn,
    string? Context);

public sealed record UpdateDecisionBody(string? Title, string? Prediction, double? Probability, DateOnly? ReviewOn,
    string? Context);

public sealed record ResolveDecisionBody(bool? Outcome, string? Note);

/// <summary>
/// The decision journal: log a call with how sure you are, answer on the review date whether it happened, and see
/// how well your confidence matches reality. Everything is private to the owner.
/// </summary>
internal static class DecisionEndpoints
{
    public static RouteGroupBuilder MapDecisionEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/decisions").WithTags("Decisions");

        group.MapGet("", async (string? status, int? limit, IDecisionService decisions, ICurrentUser user,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await decisions.ListAsync(user.OwnerId, status,
                    limit ?? DecisionRules.DefaultLimit, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("status", exception.Message);
            }
        }).WithName("ListDecisions");

        group.MapGet("/calibration", async (IDecisionService decisions, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await decisions.CalibrationAsync(user.OwnerId, ct))).WithName("GetDecisionCalibration");

        group.MapGet("/{id:guid}", async (Guid id, IDecisionService decisions, ICurrentUser user,
            CancellationToken ct) =>
            await decisions.GetAsync(id, user.OwnerId, ct) is { } decision ? Results.Ok(decision) : Results.NotFound())
            .WithName("GetDecision");

        group.MapPost("", async (CreateDecisionBody body, IDecisionService decisions, IAuditEventStore audit,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (body.Probability is null) return EndpointHelpers.Invalid("probability", "Probability is required.");
            if (body.ReviewOn is null) return EndpointHelpers.Invalid("reviewOn", "A review date is required.");
            try
            {
                var created = await decisions.CreateAsync(user.OwnerId, new CreateDecisionRequest(
                    body.Title ?? string.Empty, body.Prediction ?? string.Empty, body.Probability.Value,
                    body.ReviewOn.Value, body.Context), ct);
                await AuditAsync(audit, logger, user, "decision.created", created.Id, ct);
                return Results.Created($"/api/v1/decisions/{created.Id}", created);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid(exception.ParamName ?? "decision", exception.Message);
            }
        }).WithName("CreateDecision");

        group.MapPut("/{id:guid}", async (Guid id, UpdateDecisionBody body, IDecisionService decisions,
            ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                var updated = await decisions.UpdateAsync(id, user.OwnerId, new UpdateDecisionRequest(body.Title,
                    body.Context, body.Prediction, body.Probability, body.ReviewOn), ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid(exception.ParamName ?? "decision", exception.Message);
            }
        }).WithName("UpdateDecision");

        group.MapPost("/{id:guid}/resolve", async (Guid id, ResolveDecisionBody body, IDecisionService decisions,
            IAuditEventStore audit, ICurrentUser user, CancellationToken ct) =>
        {
            if (body.Outcome is null) return EndpointHelpers.Invalid("outcome", "Say whether it happened.");
            try
            {
                var resolved = await decisions.ResolveAsync(id, user.OwnerId, body.Outcome.Value, body.Note, ct);
                if (resolved is null) return Results.NotFound();
                await AuditAsync(audit, logger, user, "decision.resolved", id, ct);
                return Results.Ok(resolved);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("note", exception.Message);
            }
        }).WithName("ResolveDecision");

        group.MapDelete("/{id:guid}", async (Guid id, IDecisionService decisions, IAuditEventStore audit,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (!await decisions.DeleteAsync(id, user.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, user, "decision.deleted", id, ct);
            return Results.NoContent();
        }).WithName("DeleteDecision");

        return api;
    }

    // Audit events carry the id only, never the prediction or the outcome.
    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser user, string action, Guid id,
        CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, user.OwnerId, "decisions", action, "low", true, null,
            JsonSerializer.Serialize(new { decisionId = id }), ct);
}
