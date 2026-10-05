using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.People;
using Jarvis.Application.People.Radar;

namespace Jarvis.Api.Endpoints;

public sealed record PersonLinkRequest(Guid? ConnectionId, string? ChatId);

public sealed record RadarSettingsRequest(bool? ToneEnabled);

/// <summary>
/// The relationship radar: link a person to their WhatsApp chat and see how the two of you keep in touch, from
/// message times alone. Only chats the owner reads along with have messages. Audit entries carry ids, never names
/// or chat ids.
/// </summary>
internal static class PeopleRadarEndpoints
{
    public static RouteGroupBuilder MapPeopleRadarEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var people = api.MapGroup("/people").WithTags("People radar");

        people.MapGet("/radar", async (IRelationshipRadarService radar, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await radar.OverviewAsync(user.OwnerId, ct))).WithName("GetPeopleRadar");

        people.MapPut("/radar/settings", async (RadarSettingsRequest request, IRelationshipRadarService radar,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (request.ToneEnabled is null) return EndpointHelpers.Invalid("toneEnabled", "Say whether to check tone.");
            return Results.Ok(await radar.SaveSettingsAsync(user.OwnerId, request.ToneEnabled.Value, ct));
        }).WithName("SavePeopleRadarSettings");

        people.MapGet("/link-suggestions", async (IRelationshipRadarService radar, ICurrentUser user,
            CancellationToken ct) =>
            Results.Ok(await radar.SuggestAsync(user.OwnerId, ct))).WithName("ListPeopleLinkSuggestions");

        people.MapGet("/link-candidates", async (IRelationshipRadarService radar, ICurrentUser user,
            CancellationToken ct) =>
            Results.Ok(await radar.CandidatesAsync(user.OwnerId, ct))).WithName("ListPeopleLinkCandidates");

        people.MapGet("/{id:guid}/radar", async (Guid id, IRelationshipRadarService radar, ICurrentUser user,
            CancellationToken ct) =>
            await radar.PersonAsync(user.OwnerId, id, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .WithName("GetPersonRadar");

        people.MapGet("/{id:guid}/links", async (Guid id, IRelationshipRadarService radar, ICurrentUser user,
            CancellationToken ct) =>
            await radar.LinksAsync(user.OwnerId, id, ct) is { } links ? Results.Ok(links) : Results.NotFound())
            .WithName("ListPersonLinks");

        people.MapPost("/{id:guid}/links", async (Guid id, PersonLinkRequest request, IRelationshipRadarService radar,
            IAuditEventStore audit, ICurrentUser user, CancellationToken ct) =>
        {
            if (request.ConnectionId is null) return EndpointHelpers.Invalid("connectionId", "Pick a chat to link.");
            var result = await radar.LinkAsync(user.OwnerId, id, request.ConnectionId.Value,
                request.ChatId ?? string.Empty, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, user, "person.chat_linked", id, ct);
            return Results.Created($"/api/v1/people/{id}/links/{result.Value!.Id}", result.Value);
        }).WithName("LinkPersonChat");

        people.MapDelete("/{id:guid}/links/{linkId:guid}", async (Guid id, Guid linkId,
            IRelationshipRadarService radar, IAuditEventStore audit, ICurrentUser user, CancellationToken ct) =>
        {
            if (!await radar.UnlinkAsync(user.OwnerId, id, linkId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, user, "person.chat_unlinked", id, ct);
            return Results.NoContent();
        }).WithName("UnlinkPersonChat");

        return api;
    }

    private static IResult? Failed<T>(PeopleOperation<T> result) => result.Failure switch
    {
        PeopleFailure.None => null,
        PeopleFailure.NotFound => Results.NotFound(),
        PeopleFailure.Conflict => ApiProblemResults.Conflict(result.Message ?? "That chat is already linked."),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser user, string action,
        Guid personId, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, user.OwnerId, "people", action, "low", true, null,
            JsonSerializer.Serialize(new { resourceId = personId, source = "app" }), ct);
}
