using Jarvis.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CalendarFeedCacheTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-000000000001");
    private const string Url = "https://calendar.example.com/feed.ics";

    [Fact]
    public async Task Concurrent_reads_share_one_download_and_reuse_it_until_it_expires()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-03T08:00:00Z"));
        var cache = new CalendarFeedCache(time);
        var downloads = 0;
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> Download(CancellationToken _) { Interlocked.Increment(ref downloads); return gate.Task; }

        var readers = Enumerable.Range(0, 10)
            .Select(_ => cache.GetAsync(Owner, Url, null, Download, TestContext.Current.CancellationToken))
            .ToArray();
        gate.SetResult("BEGIN:VCALENDAR");
        Assert.All(await Task.WhenAll(readers), ics => Assert.Equal("BEGIN:VCALENDAR", ics));
        Assert.Equal(1, downloads);

        time.Advance(CalendarFeedCache.Lifetime - TimeSpan.FromSeconds(1));
        await cache.GetAsync(Owner, Url, null, Download, TestContext.Current.CancellationToken);
        Assert.Equal(1, downloads);

        time.Advance(TimeSpan.FromSeconds(2));
        await cache.GetAsync(Owner, Url, null, Download, TestContext.Current.CancellationToken);
        Assert.Equal(2, downloads);
    }

    [Fact]
    public async Task A_failed_download_is_retried_after_a_short_pause_not_on_every_read()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-03T08:00:00Z"));
        var cache = new CalendarFeedCache(time);
        var downloads = 0;
        Task<string> Failing(CancellationToken _)
        {
            Interlocked.Increment(ref downloads);
            return Task.FromException<string>(new HttpRequestException("calendar host is down"));
        }

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cache.GetAsync(Owner, Url, null, Failing, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cache.GetAsync(Owner, Url, null, Failing, TestContext.Current.CancellationToken));
        Assert.Equal(1, downloads);

        time.Advance(CalendarFeedCache.FailureLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal("ok", await cache.GetAsync(Owner, Url, null, _ => Task.FromResult("ok"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_new_feed_url_or_token_or_owner_is_downloaded_fresh()
    {
        var cache = new CalendarFeedCache();
        var downloads = 0;
        Task<string> Download(CancellationToken _) => Task.FromResult($"feed-{Interlocked.Increment(ref downloads)}");
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("feed-1", await cache.GetAsync(Owner, Url, null, Download, ct));
        Assert.Equal("feed-2", await cache.GetAsync(Owner, Url + "?v=2", null, Download, ct));
        Assert.Equal("feed-3", await cache.GetAsync(Owner, Url, "Bearer new", Download, ct));
        Assert.Equal("feed-4", await cache.GetAsync(Guid.CreateVersion7(), Url, null, Download, ct));
        Assert.Equal("feed-1", await cache.GetAsync(Owner, Url, null, Download, ct));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
