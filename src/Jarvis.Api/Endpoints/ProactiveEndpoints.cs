using Jarvis.Application.Conversations;
using Jarvis.Application.Proactivity;

namespace Jarvis.Api.Endpoints;

public sealed record ProactiveCommandRequest(string Command, long Revision);
public sealed record ProactiveFeedbackRequest(string Signal, long Revision);
public sealed record ProactiveUndoRequest(long Revision);

internal static class ProactiveEndpoints
{
    public static RouteGroupBuilder MapProactiveEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/proactivity/cases").WithTags("Proactive work");
        group.MapGet("", async (IProactiveService service, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user.OwnerId, ct)));
        group.MapGet("/kinds", () => Results.Ok(ProactiveKinds.All));
        group.MapGet("/sources", async (IProactiveSourceReader sources, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await sources.AvailableAsync(user.OwnerId, ct)));
        group.MapGet("/{id:guid}", async (Guid id, IProactiveService service, ICurrentUser user, CancellationToken ct) =>
            await service.GetAsync(user.OwnerId, id, ct) is { } value ? Results.Ok(value) : Results.NotFound());
        group.MapPost("", async (ProactiveCaseDraft request, IProactiveService service, ICurrentUser user, CancellationToken ct) =>
            await Respond(async () => await service.CreateAsync(user.OwnerId, request, ct)));
        group.MapPut("/{id:guid}", async (Guid id, long revision, ProactiveCaseDraft request, IProactiveService service,
            ICurrentUser user, CancellationToken ct) => await Respond(() => service.UpdateAsync(user.OwnerId, id, request, revision, ct)));
        group.MapPost("/{id:guid}/commands", async (Guid id, ProactiveCommandRequest request, IProactiveService service,
            ICurrentUser user, CancellationToken ct) => await Respond(() => service.CommandAsync(user.OwnerId, id, request.Command, request.Revision, ct)));
        group.MapPost("/{id:guid}/check", async (Guid id, IProactiveService service, ICurrentUser user, CancellationToken ct) =>
            await Respond(() => service.CheckAsync(user.OwnerId, id, ct)));
        group.MapPost("/{id:guid}/feedback", async (Guid id, ProactiveFeedbackRequest request, IProactiveService service,
            ICurrentUser user, CancellationToken ct) => await Respond(() => service.FeedbackAsync(user.OwnerId, id, request.Signal, request.Revision, ct)));
        group.MapPost("/{id:guid}/receipts/{receipt:guid}/undo", async (Guid id, Guid receipt, ProactiveUndoRequest request,
            IProactiveService service, ICurrentUser user, CancellationToken ct) =>
            await Respond(() => service.UndoAsync(user.OwnerId, id, receipt, request.Revision, ct)));
        group.MapPost("/{id:guid}/memory-resolution", async (Guid id, ProactiveMemoryChoice request,
            [Microsoft.AspNetCore.Mvc.FromServices] ProactiveMemoryResolution service, ICurrentUser user, CancellationToken ct) =>
            await Respond(() => service.ResolveAsync(user.OwnerId, id, request, ct)));
        return api;
    }

    private static async Task<IResult> Respond(Func<Task<ProactiveCase?>> action)
    {
        try { return await action() is { } value ? Results.Ok(value) : Results.NotFound(); }
        catch (ProactiveConflictException error) { return Results.Conflict(new { message = error.Message }); }
        catch (ArgumentException error) { return EndpointHelpers.Invalid("case", error.Message); }
    }
}
