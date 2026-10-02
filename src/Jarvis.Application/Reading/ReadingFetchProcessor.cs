using Jarvis.Domain.Reading;

namespace Jarvis.Application.Reading;

/// <summary>
/// Fetches one saved link, measures how long it takes to read, and asks the model for a summary. A summary that fails
/// does not fail the item: it keeps the page's own description as the excerpt instead.
/// </summary>
public sealed class ReadingFetchProcessor(
    IReadingRepository items,
    IReadingPageFetcher fetcher,
    IReadingSummarizer summarizer,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ReadingItem> ProcessAsync(ReadingItem item, CancellationToken cancellationToken)
    {
        FetchedPage page;
        try
        {
            page = await fetcher.FetchAsync(item.Url, cancellationToken);
        }
        catch (ReadingFetchException exception)
        {
            return await FailAsync(item, exception.Message, exception.Permanent, cancellationToken);
        }

        var words = ReadingPageText.CountWords(page.Text);
        if (words == 0)
            return await FailAsync(item, "The page has no readable text.", true, cancellationToken);

        var summary = await summarizer.SummarizeAsync(item.OwnerId, page, cancellationToken);
        var now = clock.GetUtcNow();
        var excerpt = Limit(page.Description, ReadingRules.MaxExcerptLength) ??
                      Limit(page.Text, ReadingRules.MaxExcerptLength);
        var ready = item with
        {
            Status = ReadingStatuses.Ready,
            Title = Limit(page.Title, ReadingRules.MaxTitleLength) ?? item.Title,
            SiteName = Limit(page.SiteName, 120) ?? item.SiteName,
            Excerpt = excerpt,
            Summary = summary is null ? null : Limit(summary.Summary, ReadingRules.MaxSummaryLength),
            KeyPoints = summary?.KeyPoints
                .Select(point => Limit(point, ReadingRules.MaxKeyPointLength))
                .Where(point => point is not null)
                .Select(point => point!)
                .Take(ReadingRules.MaxKeyPoints)
                .ToArray() ?? [],
            WordCount = words,
            ReadingMinutes = ReadingRules.ReadingMinutes(words),
            FailureReason = null,
            FetchAttempts = item.FetchAttempts + 1,
            NextFetchAt = null,
            FetchedAt = now,
            UpdatedAt = now
        };
        await items.SaveAsync(ready, cancellationToken);
        return ready;
    }

    private async Task<ReadingItem> FailAsync(ReadingItem item, string reason, bool permanent,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var attempts = item.FetchAttempts + 1;
        var giveUp = permanent || attempts >= ReadingRules.MaxFetchAttempts;
        var updated = item with
        {
            Status = giveUp ? ReadingStatuses.Failed : ReadingStatuses.Pending,
            FailureReason = Limit(reason, 200),
            FetchAttempts = attempts,
            NextFetchAt = giveUp ? null : now + RetryDelays[Math.Min(attempts - 1, RetryDelays.Length - 1)],
            UpdatedAt = now
        };
        await items.SaveAsync(updated, cancellationToken);
        return updated;
    }

    private static string? Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }
}
