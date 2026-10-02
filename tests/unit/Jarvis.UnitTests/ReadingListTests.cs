using Jarvis.Agents.Reading;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Reading;
using Jarvis.Domain.Reading;
using Jarvis.Workflows.Reading;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ReadingListTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://example.com/post?utm_source=x&id=4#comments", "https://example.com/post?id=4")]
    [InlineData("example.com/article", "https://example.com/article")]
    [InlineData("http://news.example.org/a?fbclid=abc", "http://news.example.org/a")]
    public void Links_are_cleaned_before_saving(string input, string expected) =>
        Assert.Equal(expected, ReadingUrls.Normalize(input));

    [Theory]
    [InlineData("http://localhost/admin")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.0.0.5/secret")]
    [InlineData("https://[::1]/")]
    [InlineData("https://printer.local/")]
    [InlineData("https://intranet/")]
    [InlineData("https://router.lan/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    public void Local_private_and_unusual_links_are_refused(string url) =>
        Assert.Throws<ArgumentException>(() => ReadingUrls.Normalize(url));

    [Fact]
    public void Http_www_and_trailing_slash_variants_share_a_key() =>
        Assert.Equal(ReadingUrls.Key(ReadingUrls.Normalize("https://www.Example.com/post/")),
            ReadingUrls.Key(ReadingUrls.Normalize("http://example.com/post")));

    [Fact]
    public void Html_pages_yield_their_title_site_and_article_text()
    {
        var words = string.Join(' ', Enumerable.Repeat("Interesting sentence about gardens.", 30));
        var html = $"""
            <html><head><title>Fallback</title>
            <meta property="og:title" content="How to grow tomatoes &amp; herbs">
            <meta property="og:site_name" content="Garden Weekly">
            <meta name="description" content="A short guide.">
            <script>var secret = "do not read";</script></head>
            <body><nav>Home | About</nav><article><h1>Tomatoes</h1><p>{words}</p>
            <aside>Subscribe now</aside></article><footer>Copyright</footer></body></html>
            """;

        var page = ReadingPageText.FromHtml("https://garden.example/tomatoes", html);

        Assert.Equal("How to grow tomatoes & herbs", page.Title);
        Assert.Equal("Garden Weekly", page.SiteName);
        Assert.Equal("A short guide.", page.Description);
        Assert.StartsWith("Tomatoes\nInteresting sentence", page.Text);
        Assert.DoesNotContain("secret", page.Text);
        Assert.DoesNotContain("Subscribe", page.Text);
        Assert.DoesNotContain("Home | About", page.Text);
        Assert.Equal(121, ReadingPageText.CountWords(page.Text));
    }

    [Fact]
    public void Pages_without_metadata_fall_back_to_the_title_tag_and_host()
    {
        var page = ReadingPageText.FromHtml("https://www.blog.example/p",
            "<html><head><title> My  post </title></head><body><p>Hello world</p></body></html>");

        Assert.Equal("My post", page.Title);
        Assert.Equal("blog.example", page.SiteName);
        Assert.Equal("Hello world", page.Text);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(230, 1)]
    [InlineData(345, 2)]
    [InlineData(2300, 10)]
    public void Reading_time_uses_230_words_per_minute(int words, int minutes) =>
        Assert.Equal(minutes, ReadingRules.ReadingMinutes(words));

    [Fact]
    public async Task Saving_the_same_link_twice_keeps_one_item_and_reopens_a_read_one()
    {
        var (service, repository) = Create();

        var first = await service.SaveAsync(Owner, "https://example.com/a?utm_medium=share", null, "app", default);
        await service.UpdateAsync(first.Value!.Item.Id, Owner, true, null, default);
        var second = await service.SaveAsync(Owner, "http://www.example.com/a/", "for the train", "chat", default);

        Assert.False(first.Value.AlreadySaved);
        Assert.True(second.Value!.AlreadySaved);
        var item = Assert.Single(repository.Items);
        Assert.False(item.IsRead);
        Assert.Equal("for the train", item.Note);
        Assert.Equal(ReadingStatuses.Pending, item.Status);
        Assert.Equal("https://example.com/a", item.Url);
    }

    [Fact]
    public async Task Items_are_owner_scoped()
    {
        var (service, _) = Create();
        var saved = await service.SaveAsync(Owner, "https://example.com/a", null, null, default);

        Assert.Empty(await service.ListAsync(Other, default));
        Assert.Null(await service.GetAsync(saved.Value!.Item.Id, Other, default));
        Assert.False((await service.UpdateAsync(saved.Value.Item.Id, Other, true, null, default)).Succeeded);
        Assert.False(await service.DeleteAsync(saved.Value.Item.Id, Other, default));
        Assert.Single(await service.ListAsync(Owner, default));
    }

    [Fact]
    public async Task Invalid_links_and_long_notes_are_rejected()
    {
        var (service, repository) = Create();

        var local = await service.SaveAsync(Owner, "http://192.168.1.1/", null, null, default);
        var note = await service.SaveAsync(Owner, "https://example.com", new string('x', 501), null, default);

        Assert.Equal(("url", ReadingFailure.Invalid), (local.Field, local.Failure));
        Assert.Equal(("note", ReadingFailure.Invalid), (note.Field, note.Failure));
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task A_fetched_page_gets_a_summary_key_points_and_reading_time()
    {
        var (service, repository) = Create();
        var saved = (await service.SaveAsync(Owner, "https://example.com/a", null, null, default)).Value!.Item;
        var text = string.Join(' ', Enumerable.Repeat("word", 460));
        var processor = new ReadingFetchProcessor(repository,
            new FakeFetcher(new FetchedPage("https://example.com/a", "A title", "Example", "Desc", text)),
            new FakeSummarizer(new ReadingSummary("It explains things.", ["One", " ", "Two"])), new FixedClock(Now));

        var processed = await processor.ProcessAsync(saved, default);

        Assert.Equal(ReadingStatuses.Ready, processed.Status);
        Assert.Equal("A title", processed.Title);
        Assert.Equal("It explains things.", processed.Summary);
        Assert.Equal(["One", "Two"], processed.KeyPoints);
        Assert.Equal(460, processed.WordCount);
        Assert.Equal(2, processed.ReadingMinutes);
        Assert.Equal("Desc", processed.Excerpt);
        Assert.Equal(processed, Assert.Single(repository.Items));
    }

    [Fact]
    public async Task Without_a_summary_the_item_is_still_ready_with_an_excerpt()
    {
        var (service, repository) = Create();
        var saved = (await service.SaveAsync(Owner, "https://example.com/a", null, null, default)).Value!.Item;
        var processor = new ReadingFetchProcessor(repository,
            new FakeFetcher(new FetchedPage("https://example.com/a", null, null, null, "Plain body text here.")),
            new FakeSummarizer(null), new FixedClock(Now));

        var processed = await processor.ProcessAsync(saved, default);

        Assert.Equal(ReadingStatuses.Ready, processed.Status);
        Assert.Null(processed.Summary);
        Assert.Equal("Plain body text here.", processed.Excerpt);
        Assert.Equal("example.com", processed.DisplayTitle);
    }

    [Fact]
    public async Task Temporary_failures_are_retried_and_then_marked_failed()
    {
        var (service, repository) = Create();
        var item = (await service.SaveAsync(Owner, "https://example.com/a", null, null, default)).Value!.Item;
        var processor = new ReadingFetchProcessor(repository,
            new FakeFetcher(new ReadingFetchException("The site could not be reached.", false)),
            new FakeSummarizer(null), new FixedClock(Now));

        item = await processor.ProcessAsync(item, default);
        Assert.Equal((ReadingStatuses.Pending, 1, Now.AddMinutes(1)), (item.Status, item.FetchAttempts, item.NextFetchAt));
        item = await processor.ProcessAsync(item, default);
        Assert.Equal((ReadingStatuses.Pending, Now.AddMinutes(10)), (item.Status, item.NextFetchAt));
        item = await processor.ProcessAsync(item, default);

        Assert.Equal(ReadingStatuses.Failed, item.Status);
        Assert.Equal("The site could not be reached.", item.FailureReason);
        Assert.Null(item.NextFetchAt);

        var refreshed = await service.RefreshAsync(item.Id, Owner, default);
        Assert.Equal((ReadingStatuses.Pending, 0), (refreshed.Value!.Status, refreshed.Value.FetchAttempts));
    }

    [Fact]
    public async Task Permanent_failures_are_not_retried()
    {
        var (service, repository) = Create();
        var item = (await service.SaveAsync(Owner, "https://example.com/a", null, null, default)).Value!.Item;
        var processor = new ReadingFetchProcessor(repository,
            new FakeFetcher(new ReadingFetchException("The page no longer exists.", true)),
            new FakeSummarizer(null), new FixedClock(Now));

        item = await processor.ProcessAsync(item, default);

        Assert.Equal((ReadingStatuses.Failed, 1), (item.Status, item.FetchAttempts));
    }

    [Theory]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://db.internal/")]
    public async Task The_fetcher_refuses_private_addresses_without_connecting(string url)
    {
        var exception = await Assert.ThrowsAsync<ReadingFetchException>(() =>
            new ReadingPageFetcher().FetchAsync(url, default));
        Assert.True(exception.Permanent);
    }

    [Fact]
    public void The_fetcher_honours_the_declared_charset()
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes("<meta charset=\"iso-8859-1\"><p>Café</p>");
        Assert.Contains("Café", ReadingPageFetcher.Decode(bytes, null));
    }

    [Fact]
    public void Links_are_found_in_the_users_message()
    {
        var links = ReadingTurnLinks.Extract(
            "Lees dit later: https://example.com/a?utm_source=x, en ook www.news.example/b). Niet http://localhost/x")
            .ToArray();

        Assert.Equal(["https://example.com/a", "https://www.news.example/b"], links);
    }

    [Fact]
    public async Task SaveForLater_only_saves_links_from_the_users_own_message()
    {
        var (service, repository) = Create();
        var turnLinks = new ReadingTurnLinks();
        turnLinks.Capture("lees dit later https://example.com/story");
        var audit = new RecordingAudit();
        var tools = new ReadingAgentTools(service, turnLinks, audit, new FixedUser(), NullLogger.Instance);

        var refused = await tools.SaveForLaterAsync("https://attacker.example/?data=secret");
        var saved = await tools.SaveForLaterAsync("http://www.example.com/story");

        Assert.Contains("SaveFoundLinkForLater", refused);
        Assert.StartsWith("Saved to the reading list", saved);
        var item = Assert.Single(repository.Items);
        Assert.Equal(ReadingSources.Chat, item.Source);
        Assert.Equal(["reading.saved"], audit.Actions);
        Assert.DoesNotContain("example.com", audit.Metadata[0]);
    }

    [Fact]
    public async Task The_reading_list_tool_marks_web_text_as_untrusted_and_finds_items_by_title()
    {
        var (service, repository) = Create();
        var saved = (await service.SaveAsync(Owner, "https://example.com/a", null, null, default)).Value!.Item;
        repository.Items[0] = saved with
        {
            Status = ReadingStatuses.Ready, Title = "Rust ownership explained", Summary = "Borrowing rules.",
            KeyPoints = ["Moves"], ReadingMinutes = 7
        };
        var tools = new ReadingAgentTools(service, new ReadingTurnLinks(), new RecordingAudit(), new FixedUser(),
            NullLogger.Instance);

        var list = await tools.GetReadingListAsync();
        var marked = await tools.MarkReadingItemAsync("rust ownership");

        Assert.Contains("1 unread (about 7 min in total)", list);
        Assert.Contains("untrusted data", list);
        Assert.Contains("- Summary: Borrowing rules.", list);
        Assert.Contains("as read", marked);
        Assert.True(repository.Items[0].IsRead);
    }

    [Fact]
    public void Summaries_are_parsed_from_json_with_surrounding_text()
    {
        var summary = ReadingSummarizer.Parse("""
            ```json
            {"summary": "A page about X.", "keyPoints": ["- First", 3, "Second"]}
            ```
            """);

        Assert.Equal("A page about X.", summary?.Summary);
        Assert.Equal(["First", "Second"], summary?.KeyPoints);
        Assert.Null(ReadingSummarizer.Parse("""{"summary": "", "keyPoints": []}"""));
        Assert.Null(ReadingSummarizer.Parse("not json"));
    }

    private static (ReadingListService Service, FakeReadingRepository Repository) Create()
    {
        var repository = new FakeReadingRepository();
        return (new ReadingListService(repository, new FixedClock(Now)), repository);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeFetcher : IReadingPageFetcher
    {
        private readonly FetchedPage? _page;
        private readonly ReadingFetchException? _error;

        public FakeFetcher(FetchedPage page) => _page = page;
        public FakeFetcher(ReadingFetchException error) => _error = error;

        public Task<FetchedPage> FetchAsync(string url, CancellationToken cancellationToken) =>
            _error is not null ? Task.FromException<FetchedPage>(_error) : Task.FromResult(_page!);
    }

    private sealed class FakeSummarizer(ReadingSummary? summary) : IReadingSummarizer
    {
        public Task<ReadingSummary?> SummarizeAsync(Guid ownerId, FetchedPage page,
            CancellationToken cancellationToken) => Task.FromResult(summary);
    }

    private sealed class RecordingAudit : IAuditEventStore
    {
        public List<string> Actions { get; } = [];
        public List<string> Metadata { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Actions.Add(action);
            Metadata.Add(metadataJson ?? "");
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeReadingRepository : IReadingRepository
    {
        public List<ReadingItem> Items { get; } = [];
        private readonly Dictionary<Guid, string> _keys = [];

        public Task<IReadOnlyList<ReadingItem>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReadingItem>>(Items.Where(x => x.OwnerId == ownerId)
                .OrderByDescending(x => x.CreatedAt).ToArray());

        public Task<ReadingItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<ReadingItem?> FindByKeyAsync(Guid ownerId, string urlKey, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(x => x.OwnerId == ownerId && _keys[x.Id] == urlKey));

        public Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Count(x => x.OwnerId == ownerId));

        public Task AddAsync(ReadingItem item, string urlKey, CancellationToken cancellationToken)
        {
            Items.Add(item);
            _keys[item.Id] = urlKey;
            return Task.CompletedTask;
        }

        public Task<bool> SaveAsync(ReadingItem item, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(x => x.Id == item.Id && x.OwnerId == item.OwnerId);
            if (index < 0) return Task.FromResult(false);
            Items[index] = item;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<IReadOnlyList<ReadingItem>> ListDueAsync(DateTimeOffset now, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReadingItem>>(Items
                .Where(x => x.Status == ReadingStatuses.Pending && (x.NextFetchAt is null || x.NextFetchAt <= now))
                .Take(limit).ToArray());
    }
}
