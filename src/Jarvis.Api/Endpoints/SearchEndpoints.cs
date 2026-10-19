using Jarvis.Application.Conversations;
using Jarvis.Application.Search;

namespace Jarvis.Api.Endpoints;

internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", async (IFederatedSearchService search, ICurrentUser currentUser, string? query,
                string? kinds, string? from, string? to, CancellationToken ct) =>
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

                SearchWindow? window = null;
                if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to))
                {
                    if (!TryDay(from, out var start, 0) || !TryDay(to, out var end, 1))
                        return EndpointHelpers.Invalid("from", "Use dates like 2026-10-01.");
                    if (start is { } a && end is { } b && a > b)
                        return EndpointHelpers.Invalid("from", "The start date is after the end date.");
                    window = new SearchWindow(start, end);
                }

                var response = await search.SearchAsync(currentUser.OwnerId, query!, kindFilter, ct, window);
                return Results.Ok(new FederatedSearchResponseDto(
                    response.Results.Select(ToDto).ToArray(),
                    response.Providers.Select(status => new SearchProviderStatusDto(status.ProviderId, status.Succeeded,
                        status.Error)).ToArray()));
            })
            .WithName("FederatedSearch");

        return api;
    }

    /// <summary>An empty value means open ended; the end day is inclusive, so it runs to the end of that day.</summary>
    private static bool TryDay(string? text, out DateTimeOffset? value, int endOfDay)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day)) return false;
        value = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(endOfDay).AddTicks(-endOfDay);
        return true;
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
