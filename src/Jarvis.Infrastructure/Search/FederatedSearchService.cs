using System.Diagnostics;
using Jarvis.Application.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jarvis.Infrastructure.Search;

public sealed class FederatedSearchService(
    IEnumerable<IFederatedSearchProvider> providers,
    IOptions<FederatedSearchOptions> options,
    ILogger<FederatedSearchService> logger,
    IServiceScopeFactory? scopeFactory = null) : IFederatedSearchService
{
    // Caps provider queries across all searches in this process. Each one holds a database connection, and a burst
    // of searches used to queue on the pool until providers hit their timeout and dropped out of the results.
    // The timeout now starts when a provider gets a slot, so waiting for a slot does not count against it.
    private static readonly SemaphoreSlim ProviderSlots = new(MaxConcurrentProviderQueries, MaxConcurrentProviderQueries);
    public const int MaxConcurrentProviderQueries = 32;

    private readonly FederatedSearchOptions _options = options.Value;
    private readonly IFederatedSearchProvider[] _providers = providers.ToArray();

    public async Task<FederatedSearchResponse> SearchAsync(Guid ownerId, string query,
        IReadOnlySet<string>? kindsFilter, CancellationToken cancellationToken, SearchWindow? window = null,
        Guid? conversationId = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerId, Guid.Empty);
        var normalizedQuery = SearchRanking.NormalizeQuery(query);
        if (normalizedQuery.Length is < 1 or > 2_000)
            return new FederatedSearchResponse([], []);
        if (conversationId is { } scope)
            return await SearchInConversationAsync(ownerId, scope, normalizedQuery, window, cancellationToken);

        if (kindsFilter is { Count: > 0 } && !SearchResultKinds.IsValidFilter(kindsFilter))
            return new FederatedSearchResponse([], []);

        var activeProviders = _providers.Where(provider => kindsFilter is null or { Count: 0 } ||
            provider.ResultKinds.Overlaps(kindsFilter)).ToArray();

        var providerTasks = activeProviders.Select(provider => QueryProviderAsync(provider, ownerId,
            normalizedQuery, kindsFilter, cancellationToken)).ToArray();
        var outcomes = await Task.WhenAll(providerTasks);

        var statuses = outcomes.Select(item => item.Status).ToArray();
        var merged = outcomes.SelectMany(item => item.Results)
            .Where(hit => window is null || window.Contains(hit.Timestamp)).ToList();
        var ranked = RankAndCap(merged, normalizedQuery);
        return new FederatedSearchResponse(ranked, statuses);
    }

    /// <summary>
    /// Every match inside one conversation, newest first. Only providers that know how to search a single
    /// conversation take part, and none of the cross-kind ranking or capping applies.
    /// </summary>
    private async Task<FederatedSearchResponse> SearchInConversationAsync(Guid ownerId, Guid conversationId,
        string query, SearchWindow? window, CancellationToken cancellationToken)
    {
        var statuses = new List<SearchProviderStatus>();
        var results = new List<FederatedSearchResult>();
        foreach (var provider in _providers.OfType<IConversationScopedSearchProvider>())
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.ProviderTimeout);
                await using var scope = scopeFactory?.CreateAsyncScope();
                var isolated = scope is { } owned ? Isolate(owned.ServiceProvider, provider) : provider;
                results.AddRange(await isolated.SearchInConversationAsync(ownerId, conversationId, query,
                    _options.MaxInConversation, timeout.Token));
                statuses.Add(new SearchProviderStatus(provider.ProviderId, true, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Search provider {ProviderId} failed inside a conversation for owner {OwnerId}.",
                    provider.ProviderId, ownerId);
                statuses.Add(new SearchProviderStatus(provider.ProviderId, false, "Search provider unavailable."));
            }
        }
        var ordered = results
            .Where(hit => window is null || window.Contains(hit.Timestamp))
            .OrderByDescending(hit => hit.Timestamp ?? DateTimeOffset.MinValue)
            .Take(_options.MaxInConversation)
            .ToList();
        return new FederatedSearchResponse(ordered, statuses);
    }

    private async Task<(SearchProviderStatus Status, IReadOnlyList<FederatedSearchResult> Results)> QueryProviderAsync(
        IFederatedSearchProvider provider, Guid ownerId, string query, IReadOnlySet<string>? kindsFilter,
        CancellationToken cancellationToken)
    {
        using var activity = SearchDiagnostics.ActivitySource.StartActivity("search.provider");
        activity?.SetTag("search.provider", provider.ProviderId);
        var stopwatch = Stopwatch.StartNew();
        await ProviderSlots.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ProviderTimeout);
            // Providers run concurrently, and a DbContext is not thread-safe, so each one gets its own scope.
            await using var scope = scopeFactory?.CreateAsyncScope();
            var isolated = scope is { } owned ? Isolate(owned.ServiceProvider, provider) : provider;
            var results = await isolated.SearchAsync(ownerId, query, _options.MaxPerProvider, timeout.Token);
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
        finally
        {
            ProviderSlots.Release();
        }
    }

    /// <summary>
    /// <paramref name="provider"/>'s own registration in the scope. Resolving every provider and picking one built
    /// all of them for each provider: about 25 × 25 objects per search. Unregistered types (tests) are looked up.
    /// </summary>
    public static T Isolate<T>(IServiceProvider services, T provider) where T : IFederatedSearchProvider =>
        services.GetService(provider.GetType()) is T registered
            ? registered
            : services.GetServices<IFederatedSearchProvider>().OfType<T>()
                .FirstOrDefault(candidate => candidate.ProviderId == provider.ProviderId) ?? provider;

    private List<FederatedSearchResult> RankAndCap(IReadOnlyList<FederatedSearchResult> results, string query)
    {
        // Sized for the original 13 kinds, so adding kinds does not shrink how many hits each one may show.
        var perKindCap = Math.Max(2, _options.MaxTotal / 13);
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
