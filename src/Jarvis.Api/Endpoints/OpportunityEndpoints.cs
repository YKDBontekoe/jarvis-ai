using Jarvis.Application.Conversations;
using Jarvis.Application.Opportunities;

namespace Jarvis.Api.Endpoints;

public sealed record OpportunityUnmuteRequest(string? Kind);

/// <summary>
/// The opportunity radar: a few chances to act while there is time, each with the facts behind it and where to act.
/// Reading it proposes only; feedback quiets what is not wanted.
/// </summary>
internal static class OpportunityEndpoints
{
    public static RouteGroupBuilder MapOpportunityEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/opportunities").WithTags("Opportunities");

        group.MapGet("", async (IOpportunityService service, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user.OwnerId, ct))).WithName("ListOpportunities");

        group.MapPost("/feedback", async (OpportunityFeedbackRequest request, IOpportunityService service,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Key))
                return EndpointHelpers.Invalid("key", "Say which opportunity this is about.");
            if (request.Verdict is null || !OpportunityVerdicts.All.Contains(request.Verdict))
                return EndpointHelpers.Invalid("verdict", "Choose useful, later, handled, wrong or fewer.");
            return await service.FeedbackAsync(user.OwnerId, request.Key, request.Verdict, ct)
                ? Results.NoContent()
                : Results.NotFound();
        }).WithName("GiveOpportunityFeedback");

        group.MapPost("/unmute", async (OpportunityUnmuteRequest request, IOpportunityService service,
            ICurrentUser user, CancellationToken ct) =>
            request.Kind is not null && await service.UnmuteAsync(user.OwnerId, request.Kind, ct)
                ? Results.NoContent()
                : Results.NotFound()).WithName("UnmuteOpportunityKind");

        return api;
    }
}
