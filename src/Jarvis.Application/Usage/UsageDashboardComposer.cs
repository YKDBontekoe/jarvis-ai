namespace Jarvis.Application.Usage;

public sealed record UsageModelAggregate(
    string Provider,
    string Purpose,
    string Model,
    int Calls,
    int CompletedCalls,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningOutputTokens,
    decimal CostUsd,
    int UnpricedCalls,
    int WebSearchActions,
    long DurationMs);

public sealed record UsagePoint(string Provider, long TotalTokens, decimal CostUsd, DateTimeOffset CreatedAt);

public sealed record ProviderLifetime(
    string Provider,
    int Calls,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    int UnpricedCalls,
    int WebSearchActions);

public sealed record UsageComposeInput(
    DateTimeOffset Now,
    string Period,
    string TimeZoneId,
    IReadOnlyList<UsageModelAggregate> Models,
    IReadOnlyList<UsagePoint> Points,
    IReadOnlyList<ProviderLifetime> Lifetime,
    UsageActivity Activity,
    PersonalizationSnapshot Personalization,
    ImprovementSnapshot? Improvement = null);

public static class UsageDashboardComposer
{
    public const int MaxChartDays = 62;
    public const int MaxModelRows = 12;

    public static UsageDashboard Compose(UsageComposeInput input)
    {
        var zone = UsagePeriods.ResolveZone(input.TimeZoneId);
        var from = UsagePeriods.Start(input.Period, input.Now, zone);
        var codex = Summarize(UsageProviders.Codex, input.Models, input.Lifetime, CodexNote);
        var openRouter = Summarize(UsageProviders.OpenRouter, input.Models, input.Lifetime, OpenRouterNote);
        var otherRows = input.Models.Where(row => row.Provider is not (UsageProviders.Codex or UsageProviders.OpenRouter)).ToArray();
        ProviderUsage? other = otherRows.Length == 0
            ? null
            : Summarize(null, otherRows, input.Lifetime.Where(item =>
                item.Provider is not (UsageProviders.Codex or UsageProviders.OpenRouter)).ToArray(), OtherNote);
        var activity = input.Activity with
        {
            WebSearches = new CountWindow(
                input.Models.Sum(row => row.WebSearchActions),
                input.Lifetime.Sum(item => item.WebSearchActions))
        };
        var (daily, note) = Chart(input.Points, from, input.Now, zone);
        var models = input.Models
            .OrderByDescending(row => row.InputTokens + row.OutputTokens)
            .ThenByDescending(row => row.Calls)
            .Take(MaxModelRows)
            .Select(row => new ModelUsageRow(
                row.Provider,
                string.IsNullOrWhiteSpace(row.Model) ? "Default" : row.Model,
                row.Purpose,
                row.Calls,
                row.InputTokens,
                row.OutputTokens,
                row.InputTokens + row.OutputTokens,
                PricedCost(row.Provider, row.Calls, row.UnpricedCalls, row.CostUsd)))
            .ToArray();
        return new UsageDashboard(
            input.Period, from, input.Now, zone.Id, input.Now, input.Personalization, activity,
            codex, openRouter, other, daily, note, models, input.Improvement ?? ImprovementSnapshot.Empty);
    }

    private const string CodexNote =
        "Included with your ChatGPT subscription. Codex does not bill per token.";

    private const string OpenRouterNote =
        "Estimated from OpenRouter's published prices when each call was made. Cached input is included at the prompt price.";

    private const string OtherNote =
        "Server-side embeddings and other endpoints that do not publish a per-token price.";

    private static ProviderUsage Summarize(string? provider, IReadOnlyList<UsageModelAggregate> rows,
        IReadOnlyList<ProviderLifetime> lifetime, string note)
    {
        var mine = provider is null ? rows : rows.Where(row => row.Provider == provider).ToArray();
        var calls = mine.Sum(row => row.Calls);
        var completed = mine.Sum(row => row.CompletedCalls);
        var unpriced = mine.Sum(row => row.UnpricedCalls);
        var input = mine.Sum(row => row.InputTokens);
        var output = mine.Sum(row => row.OutputTokens);
        var duration = mine.Sum(row => row.DurationMs);
        var life = provider is null
            ? lifetime
            : lifetime.Where(item => item.Provider == provider).ToArray();
        var lifeCalls = life.Sum(item => item.Calls);
        var lifeUnpriced = life.Sum(item => item.UnpricedCalls);
        var lifeCost = life.Sum(item => item.CostUsd);
        var pricedProvider = provider == UsageProviders.OpenRouter;
        var costNote = note;
        if (provider == UsageProviders.OpenRouter && unpriced > 0)
            costNote += unpriced == 1
                ? " 1 call could not be priced."
                : $" {unpriced} calls could not be priced.";
        return new ProviderUsage(
            calls,
            completed,
            Math.Max(0, calls - completed),
            input,
            output,
            mine.Sum(row => row.CachedInputTokens),
            mine.Sum(row => row.ReasoningOutputTokens),
            input + output,
            pricedProvider ? PricedCost(UsageProviders.OpenRouter, calls, unpriced, mine.Sum(row => row.CostUsd)) : null,
            unpriced,
            mine.Sum(row => row.WebSearchActions),
            calls == 0 ? 0 : (int)Math.Clamp(duration / calls, 0, int.MaxValue),
            costNote,
            lifeCalls,
            life.Sum(item => item.InputTokens + item.OutputTokens),
            pricedProvider ? PricedCost(UsageProviders.OpenRouter, lifeCalls, lifeUnpriced, lifeCost) : null,
            mine.GroupBy(row => string.IsNullOrWhiteSpace(row.Purpose) ? UsagePurposes.Chat : row.Purpose)
                .Select(group => new PurposeCount(group.Key, group.Sum(row => row.Calls)))
                .OrderByDescending(item => item.Calls)
                .ToArray());
    }

    private static decimal? PricedCost(string provider, int calls, int unpriced, decimal cost)
    {
        if (provider != UsageProviders.OpenRouter) return null;
        var priced = calls - unpriced;
        return priced <= 0 ? null : decimal.Round(cost, 8, MidpointRounding.AwayFromZero);
    }

    private static (IReadOnlyList<DailyUsagePoint> Points, string? Note) Chart(
        IReadOnlyList<UsagePoint> points, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        var end = LocalDay(to, zone);
        var start = from <= DateTimeOffset.UnixEpoch
            ? (points.Count == 0 ? end : points.Min(point => LocalDay(point.CreatedAt, zone)))
            : LocalDay(from, zone);
        if (end < start) end = start;
        string? note = null;
        if (end.DayNumber - start.DayNumber + 1 > MaxChartDays)
        {
            start = end.AddDays(1 - MaxChartDays);
            note = "The chart shows the last 62 days. Totals above cover the full period.";
        }

        var buckets = new Dictionary<DateOnly, (long Codex, long OpenRouter, decimal Cost)>();
        foreach (var point in points)
        {
            var day = LocalDay(point.CreatedAt, zone);
            if (day < start || day > end) continue;
            buckets.TryGetValue(day, out var current);
            if (point.Provider == UsageProviders.Codex) current.Codex += point.TotalTokens;
            else if (point.Provider == UsageProviders.OpenRouter)
            {
                current.OpenRouter += point.TotalTokens;
                current.Cost += point.CostUsd;
            }
            buckets[day] = current;
        }

        var days = new List<DailyUsagePoint>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            buckets.TryGetValue(day, out var value);
            days.Add(new DailyUsagePoint(day, value.Codex, value.OpenRouter, value.Cost));
        }
        return (days, note);
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
