using Jarvis.Application.Conversations;
using Jarvis.Application.Search;
using Jarvis.Application.Navigation;

namespace Jarvis.Api.Endpoints;

internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/navigation", async (NavigationService navigation, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await navigation.SuggestionsAsync(user.OwnerId, ct))).WithName("NavigationSuggestions");
        api.MapPost("/navigation/resolve", async (NavigationRequest request, NavigationService navigation,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Request) || request.Request.Trim().Length > 1_000)
                return EndpointHelpers.Invalid("request", "Use 1 to 1,000 characters.");
            return Results.Ok(await navigation.ResolveAsync(user.OwnerId, request.Request, ct));
        }).WithName("ResolveNavigationIntent");
        api.MapGet("/search", async (IFederatedSearchService search, ICurrentUser currentUser, string? query,
                string? kinds, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return EndpointHelpers.Invalid("query", "Query must contain 1 to 2,000 characters.");
                IReadOnlySet<string>? kindFilter = null;
                if (!string.IsNullOrWhiteSpace(kinds))
                {
                    var parsed = kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);
                    if (!SearchResultKinds.IsValidFilter(parsed))
                        return EndpointHelpers.Invalid("kinds", "Choose supported search result kinds.");
                    kindFilter = parsed;
                }

                var response = await search.SearchAsync(currentUser.OwnerId, query!, kindFilter, ct);
                return Results.Ok(new FederatedSearchResponseDto(
                    response.Results.Select(ToDto).ToArray(),
                    response.Providers.Select(status => new SearchProviderStatusDto(status.ProviderId, status.Succeeded,
                        status.Error)).ToArray()));
            })
            .WithName("FederatedSearch");

        return api;
    }

    private static FederatedSearchResultDto ToDto(FederatedSearchResult result) => new(
        result.Kind,
        result.Id,
        result.Title,
        result.Summary,
        result.Timestamp,
        new SearchRouteTargetDto(result.Route.Kind,
            new Dictionary<string, string>(result.Route.Parameters)),
        result.Relevance,
        result.IsPinned);
}

public sealed record NavigationRequest(string? Request);

public sealed record FederatedSearchResponseDto(
    IReadOnlyList<FederatedSearchResultDto> Results,
    IReadOnlyList<SearchProviderStatusDto> Providers);

public sealed record FederatedSearchResultDto(
    string Kind,
    string Id,
    string Title,
    string? Summary,
    DateTimeOffset? Timestamp,
    SearchRouteTargetDto Route,
    double Relevance,
    bool IsPinned);

public sealed record SearchRouteTargetDto(string Kind, IReadOnlyDictionary<string, string> Parameters);

public sealed record SearchProviderStatusDto(string ProviderId, bool Succeeded, string? Error);
