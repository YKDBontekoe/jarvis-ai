using Jarvis.Application.Search;
using Jarvis.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class FederatedSearchTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOwner = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Empty_or_whitespace_query_returns_no_results()
    {
        var service = CreateService(new StubProvider("a", SearchResultKinds.Conversation, Owner, "hello"));
        var response = await service.SearchAsync(Owner, "   ", null, CancellationToken.None);
        Assert.Empty(response.Results);
        Assert.Empty(response.Providers);
    }

    [Fact]
    public async Task Unicode_query_matches_normalized_titles()
    {
        var hit = Result(SearchResultKinds.Memory, "1", "café notes", 0.8);
        var service = CreateService(new StubProvider("memories", SearchResultKinds.Memory, Owner, "café",
            _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([hit])));
        var response = await service.SearchAsync(Owner, "CAFÉ", null, CancellationToken.None);
        Assert.Equal("1", Assert.Single(response.Results).Id);
    }

    [Fact]
    public async Task Owner_id_is_required_at_boundary()
    {
        var service = CreateService(new StubProvider("memories", SearchResultKinds.Memory, Owner, "x"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SearchAsync(Guid.Empty, "query", null, CancellationToken.None));
    }

    [Fact]
    public async Task Providers_are_scoped_with_owner_id()
    {
        var provider = new RecordingProvider();
        var service = CreateService(provider);
        await service.SearchAsync(Owner, "alpha", null, CancellationToken.None);
        Assert.Equal(Owner, Assert.Single(provider.OwnerIds));
        await service.SearchAsync(OtherOwner, "beta", null, CancellationToken.None);
        Assert.Equal(OtherOwner, provider.OwnerIds[^1]);
    }

    [Fact]
    public async Task Partial_provider_failure_returns_other_results_and_sanitized_error()
    {
        var good = Result(SearchResultKinds.Task, "t1", "Deploy", 0.7);
        var service = CreateService(
            new ThrowingProvider("broken"),
            new StubProvider("tasks", SearchResultKinds.Task, Owner, "deploy",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([good])));
        var response = await service.SearchAsync(Owner, "deploy", null, CancellationToken.None);
        Assert.Equal("t1", Assert.Single(response.Results).Id);
        var failed = Assert.Single(response.Providers, status => !status.Succeeded);
        Assert.Equal("broken", failed.ProviderId);
        Assert.Equal("Search provider unavailable.", failed.Error);
        Assert.DoesNotContain("Exception", failed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_timeout_returns_partial_results()
    {
        var service = CreateServiceWithOptions([
            new SlowProvider(TimeSpan.FromSeconds(5)),
            new StubProvider("tasks", SearchResultKinds.Task, Owner, "ship",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([
                    Result(SearchResultKinds.Task, "t2", "Ship release", 0.6)
                ])),
        ], new FederatedSearchOptions { ProviderTimeout = TimeSpan.FromMilliseconds(80) });
        var response = await service.SearchAsync(Owner, "ship", null, CancellationToken.None);
        Assert.Equal("t2", Assert.Single(response.Results).Id);
        Assert.Contains(response.Providers, status => status.ProviderId == "slow" && !status.Succeeded);
    }

    [Fact]
    public async Task Kind_filter_limits_providers()
    {
        var memory = Result(SearchResultKinds.Memory, "m1", "Pinned habit", 0.9, pinned: true);
        var task = Result(SearchResultKinds.Task, "t1", "Pinned habit task", 0.5);
        var service = CreateService(
            new StubProvider("memories", SearchResultKinds.Memory, Owner, "habit",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([memory])),
            new StubProvider("tasks", SearchResultKinds.Task, Owner, "habit",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([task])));
        var response = await service.SearchAsync(Owner, "habit",
            new HashSet<string> { SearchResultKinds.Memory }, CancellationToken.None);
        Assert.Equal(SearchResultKinds.Memory, Assert.Single(response.Results).Kind);
        Assert.Single(response.Providers);
    }

    [Fact]
    public async Task Ranking_prefers_exact_title_and_pin_without_one_provider_dominating()
    {
        var noisy = Enumerable.Range(0, 20).Select(index =>
            Result(SearchResultKinds.File, $"f{index}", $"noise {index}", 0.99)).ToArray();
        var exact = Result(SearchResultKinds.Reminder, "r1", "Quarterly review", 0.2);
        var pinned = Result(SearchResultKinds.Memory, "m1", "Quarterly review notes", 0.3, pinned: true);
        var service = CreateServiceWithOptions([
            new StubProvider("files", SearchResultKinds.File, Owner, "quarterly",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>(noisy)),
            new StubProvider("reminders", SearchResultKinds.Reminder, Owner, "quarterly",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([exact])),
            new StubProvider("memories", SearchResultKinds.Memory, Owner, "quarterly",
                _ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([pinned])),
        ], new FederatedSearchOptions { MaxTotal = 10, MaxPerProvider = 8 });
        var response = await service.SearchAsync(Owner, "Quarterly review", null, CancellationToken.None);
        Assert.True(response.Results.Count <= 10);
        Assert.Contains(response.Results, hit => hit.Id == "r1");
        Assert.Contains(response.Results, hit => hit.Id == "m1");
        Assert.Contains(response.Results.Take(4), hit => hit.Id is "r1" or "m1");
        Assert.InRange(response.Results.Count(hit => hit.Kind == SearchResultKinds.File), 0, 2);
    }

    [Fact]
    public void Results_do_not_carry_sensitive_field_names()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Result(SearchResultKinds.Memory, "m1", "safe", 1,
            summary: "visible only"));
        Assert.DoesNotContain("ArgumentsJson", json, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", json, StringComparison.OrdinalIgnoreCase);
    }

    private static FederatedSearchService CreateService(params IFederatedSearchProvider[] providers) =>
        CreateServiceWithOptions(providers, new FederatedSearchOptions());

    private static FederatedSearchService CreateServiceWithOptions(IFederatedSearchProvider[] providers,
        FederatedSearchOptions options) =>
        new(providers, Options.Create(options), NullLogger<FederatedSearchService>.Instance);

    private static FederatedSearchResult Result(string kind, string id, string title, double relevance,
        bool pinned = false, string? summary = null) => new(kind, id, title, summary, DateTimeOffset.UtcNow,
        new SearchRouteTarget(kind, new Dictionary<string, string> { ["id"] = id }), relevance, pinned);

    private sealed class StubProvider : IFederatedSearchProvider
    {
        private readonly Func<Guid, Task<IReadOnlyList<FederatedSearchResult>>> _search;

        public StubProvider(string providerId, string kind, Guid expectedOwner, string query,
            Func<Guid, Task<IReadOnlyList<FederatedSearchResult>>>? search = null)
        {
            ProviderId = providerId;
            ResultKinds = new HashSet<string> { kind };
            _search = search ?? (_ => Task.FromResult<IReadOnlyList<FederatedSearchResult>>([
                Result(kind, "1", query, 0.5)
            ]));
            ExpectedOwner = expectedOwner;
        }

        public Guid ExpectedOwner { get; }
        public string ProviderId { get; }
        public IReadOnlySet<string> ResultKinds { get; }

        public Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
            CancellationToken cancellationToken)
        {
            Assert.Equal(ExpectedOwner, ownerId);
            return _search(ownerId);
        }
    }

    private sealed class ThrowingProvider(string providerId) : IFederatedSearchProvider
    {
        public string ProviderId => providerId;
        public IReadOnlySet<string> ResultKinds => SearchResultKinds.All;

        public Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database connection string password=secret");
    }

    private sealed class SlowProvider(TimeSpan delay) : IFederatedSearchProvider
    {
        public string ProviderId => "slow";
        public IReadOnlySet<string> ResultKinds => SearchResultKinds.All;

        public async Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return [Result(SearchResultKinds.File, "slow", "late", 1)];
        }
    }

    private sealed class RecordingProvider : IFederatedSearchProvider
    {
        public List<Guid> OwnerIds { get; } = [];
        public string ProviderId => "record";
        public IReadOnlySet<string> ResultKinds => SearchResultKinds.All;

        public Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
            CancellationToken cancellationToken)
        {
            OwnerIds.Add(ownerId);
            return Task.FromResult<IReadOnlyList<FederatedSearchResult>>([]);
        }
    }
}
