using Jarvis.Application.Conversations;
using Jarvis.Application.Events;

namespace Jarvis.Api.Endpoints;

public sealed record LinkEntitiesBody(string? From, string? To, string? Relation);

/// <summary>
/// The event spine as the app sees it: the owner's activity feed, everything related to one thing, and links the
/// owner draws between things. Every read and write is scoped to the signed-in owner.
/// </summary>
internal static class EventEndpoints
{
    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var events = api.MapGroup("/events").WithTags("Events");

        events.MapGet("", async (DateTimeOffset? since, string? kinds, string? origins, string? subject, int? limit,
            IOwnerEventRepository repository, ICurrentUser user, CancellationToken ct) =>
        {
            var kindList = Split(kinds);
            var originList = new List<EventOrigin>();
            foreach (var value in Split(origins) ?? [])
            {
                if (!Enum.TryParse<EventOrigin>(value, ignoreCase: true, out var origin))
                    return EndpointHelpers.Invalid("origins", $"Unknown origin '{value}'.");
                originList.Add(origin);
            }
            if (subject is not null && !EntityRef.TryParse(subject, out _))
                return EndpointHelpers.Invalid("subject", "Use type:id, for example task:<id>.");
            return Results.Ok(await repository.ListAsync(user.OwnerId,
                new OwnerEventQuery(since, kindList, originList, subject, Math.Clamp(limit ?? 50, 1,
                    JarvisEventLimits.MaxListLimit)), ct));
        }).WithName("ListOwnerEvents");

        var entities = api.MapGroup("/entities").WithTags("Events");

        entities.MapGet("/{type}/{id:guid}/related", async (string type, Guid id, IRelatedEntityService related,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (!EntityTypes.IsValid(type)) return EndpointHelpers.Invalid("type", $"Unknown type '{type}'.");
            var entity = new EntityRef(type, id);
            var title = await related.DescribeAsync(user.OwnerId, entity, ct);
            if (title is null) return Results.NotFound();
            return Results.Ok(new
            {
                @ref = entity.ToString(),
                title,
                related = await related.GetRelatedAsync(user.OwnerId, entity, ct)
            });
        }).WithName("GetRelatedEntities");

        entities.MapPost("/links", async (LinkEntitiesBody body, EntityLinker linker, ICurrentUser user,
            CancellationToken ct) => await linker.LinkAsync(user.OwnerId, body.From, body.To, body.Relation, ct) switch
        {
            LinkOutcome.Linked => Results.Created("/api/v1/entities/links", new { linked = true }),
            LinkOutcome.AlreadyLinked => Results.Ok(new { linked = true }),
            LinkOutcome.NotFound => Results.NotFound(),
            _ => EndpointHelpers.Invalid("from", "Use two different refs written type:id.")
        }).WithName("LinkEntities");

        entities.MapDelete("/links", async (string? from, string? to, string? relation, EntityLinker linker,
            ICurrentUser user, CancellationToken ct) =>
            await linker.UnlinkAsync(user.OwnerId, from, to, relation, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("UnlinkEntities");

        return api;
    }

    private static string[]? Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
