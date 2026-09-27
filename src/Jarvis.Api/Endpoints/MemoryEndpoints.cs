using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;

namespace Jarvis.Api.Endpoints;

internal static class MemoryEndpoints
{
    public static RouteGroupBuilder MapMemoryEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        api.MapGet("/memory", async (IMemoryService memory, ICurrentUser currentUser, string? kind,
                CancellationToken ct) =>
            {
                if (!MemoryKinds.IsValidFilter(kind))
                    return EndpointHelpers.Invalid("kind", "Choose a supported memory category.");
                return Results.Ok((await memory.ListAsync(currentUser.OwnerId, kind, ct)).Select(record => record.ToDto()));
            })
            .WithName("ListMemories");

        api.MapGet("/memory/search", async (IMemoryService memory, ICurrentUser currentUser, string query,
                string? kind, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(query) || query.Length > 2_000)
                    return EndpointHelpers.Invalid("query", "Query must contain 1 to 2,000 characters.");
                if (!MemoryKinds.IsValidFilter(kind))
                    return EndpointHelpers.Invalid("kind", "Choose a supported memory category.");
                return Results.Ok((await memory.SearchAsync(currentUser.OwnerId, query, ct, kind))
                    .Select(hit => new MemoryHitDto(hit.Memory.ToDto(), hit.Score)));
            })
            .WithName("SearchMemories");

        api.MapPost("/memory", async (IMemoryService memory, IAuditEventStore audit,
            ICurrentUser currentUser, MemoryRequest request, CancellationToken ct) =>
        {
            if (!Validate(request, out var errors)) return Results.ValidationProblem(errors);
            var record = await memory.CreateAsync(currentUser.OwnerId, request.Kind!, request.Content!,
                request.Importance, request.Confidence, request.ValidUntil, request.IsPinned, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "memory", "memory.created",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = record.Id, kind = record.Kind }), ct);
            return Results.Created($"/api/v1/memory/{record.Id}", record.ToDto());
        }).WithName("CreateMemory");

        api.MapGet("/memory/{id:guid}", async (Guid id, IMemoryService memory, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var record = await memory.GetAsync(id, currentUser.OwnerId, ct);
            return record is null ? Results.NotFound() : Results.Ok(record.ToDto());
        }).WithName("GetMemory");

        api.MapPut("/memory/{id:guid}", async (Guid id, IMemoryService memory, IAuditEventStore audit,
            ICurrentUser currentUser, MemoryRequest request, CancellationToken ct) =>
        {
            if (!Validate(request, out var errors)) return Results.ValidationProblem(errors);
            if (await memory.GetAsync(id, currentUser.OwnerId, ct) is null) return Results.NotFound();
            var record = await memory.UpdateAsync(id, currentUser.OwnerId, request.Kind!, request.Content!,
                request.Importance, request.Confidence, request.ValidUntil, request.IsPinned, ct);
            if (record is null) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "memory", "memory.updated",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = id, kind = record.Kind }), ct);
            return Results.Ok(record.ToDto());
        }).WithName("UpdateMemory");

        api.MapDelete("/memory/{id:guid}", async (Guid id, IMemoryService memory, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (await memory.GetAsync(id, currentUser.OwnerId, ct) is null) return Results.NotFound();
            await memory.DeleteAsync(id, currentUser.OwnerId, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "memory", "memory.deleted",
                "high", true, null, JsonSerializer.Serialize(new { resourceId = id }), ct);
            return Results.NoContent();
        }).WithName("DeleteMemory");

        return api;
    }

    private static bool Validate(MemoryRequest request, out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (string.IsNullOrWhiteSpace(request.Kind)) errors["kind"] = ["Kind is required."];
        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 8_000)
            errors["content"] = ["Content must contain 1 to 8,000 characters."];
        if (request.Importance is < 0 or > 1) errors["importance"] = ["Importance must be between 0 and 1."];
        if (request.Confidence is < 0 or > 1) errors["confidence"] = ["Confidence must be between 0 and 1."];
        if (request.Kind is not null && !MemoryKinds.IsValid(request.Kind)) errors["kind"] = ["Unknown memory kind."];
        return errors.Count == 0;
    }
}
