using System.Diagnostics;
using Jarvis.Application.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jarvis.Infrastructure.Search;

public sealed class FederatedSearchService(
    IEnumerable<IFederatedSearchProvider> providers,
    IOptions<FederatedSearchOptions> options,
    ILogger<FederatedSearchService> logger) : IFederatedSearchService
{
    private readonly FederatedSearchOptions _options = options.Value;
    private readonly IFederatedSearchProvider[] _providers = providers.ToArray();

    public async Task<FederatedSearchResponse> SearchAsync(Guid ownerId, string query,
        IReadOnlySet<string>? kindsFilter, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerId, Guid.Empty);
        var normalizedQuery = SearchRanking.NormalizeQuery(query);
        if (normalizedQuery.Length is < 1 or > 2_000)
            return new FederatedSearchResponse([], []);

        if (kindsFilter is { Count: > 0 } && !SearchResultKinds.IsValidFilter(kindsFilter))
            return new FederatedSearchResponse([], []);

        var activeProviders = _providers.Where(provider => kindsFilter is null or { Count: 0 } ||
            provider.ResultKinds.Overlaps(kindsFilter)).ToArray();

        var providerTasks = activeProviders.Select(provider => QueryProviderAsync(provider, ownerId,
            normalizedQuery, kindsFilter, cancellationToken)).ToArray();
        var outcomes = await Task.WhenAll(providerTasks);

        var statuses = outcomes.Select(item => item.Status).ToArray();
        var merged = outcomes.SelectMany(item => item.Results).ToList();
        var ranked = RankAndCap(merged, normalizedQuery);
        return new FederatedSearchResponse(ranked, statuses);
    }

    private async Task<(SearchProviderStatus Status, IReadOnlyList<FederatedSearchResult> Results)> QueryProviderAsync(
        IFederatedSearchProvider provider, Guid ownerId, string query, IReadOnlySet<string>? kindsFilter,
        CancellationToken cancellationToken)
    {
        using var activity = SearchDiagnostics.ActivitySource.StartActivity("search.provider");
        activity?.SetTag("search.provider", provider.ProviderId);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ProviderTimeout);
            var results = await provider.SearchAsync(ownerId, query, _options.MaxPerProvider, timeout.Token);
            if (kindsFilter is { Count: > 0 })
                results = results.Where(hit => kindsFilter.Contains(hit.Kind)).ToArray();
            SearchDiagnostics.ProviderLatency.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", provider.ProviderId));
            SearchDiagnostics.ProviderResultCount.Record(results.Count,
                new KeyValuePair<string, object?>("provider", provider.ProviderId));
            return (new SearchProviderStatus(provider.ProviderId, true, null), results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Search provider {ProviderId} timed out for owner {OwnerId}.",
                provider.ProviderId, ownerId);
            SearchDiagnostics.ProviderTimeouts.Add(1,
                new KeyValuePair<string, object?>("provider", provider.ProviderId));
            return (new SearchProviderStatus(provider.ProviderId, false, "Search provider timed out."), []);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Search provider {ProviderId} failed for owner {OwnerId}.",
                provider.ProviderId, ownerId);
            SearchDiagnostics.ProviderFailures.Add(1,
                new KeyValuePair<string, object?>("provider", provider.ProviderId));
            return (new SearchProviderStatus(provider.ProviderId, false, "Search provider unavailable."), []);
        }
    }

    private List<FederatedSearchResult> RankAndCap(IReadOnlyList<FederatedSearchResult> results, string query)
    {
        var perKindCap = Math.Max(2, _options.MaxTotal / Math.Max(1, SearchResultKinds.All.Count));
        var scored = results
            .Select(hit => hit with
            {
                Relevance = SearchRanking.Combine(hit.Relevance, hit.Title, query, hit.IsPinned, hit.Timestamp)
            })
            .OrderByDescending(hit => hit.Relevance)
            .ThenByDescending(hit => hit.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(hit => hit.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var kindCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var capped = new List<FederatedSearchResult>(_options.MaxTotal);
        foreach (var hit in scored)
        {
            kindCounts.TryGetValue(hit.Kind, out var kindCount);
            if (kindCount >= perKindCap) continue;
            capped.Add(hit);
            kindCounts[hit.Kind] = kindCount + 1;
            if (capped.Count >= _options.MaxTotal) break;
        }
        return capped;
    }
}
