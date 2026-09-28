namespace Jarvis.Application.Usage;

public static class UsageProviders
{
    public const string Codex = "codex";
    public const string OpenRouter = "openrouter";
    public const string Server = "server";
}

public static class UsagePurposes
{
    public const string Chat = "chat";
    public const string Background = "background";
    public const string Reasoning = "reasoning";
    public const string Vision = "vision";
    public const string Embedding = "embedding";
}

public static class UsageOutcomes
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Timeout = "timeout";
}

public static class UsagePeriods
{
    public const string Today = "today";
    public const string SevenDays = "7d";
    public const string ThirtyDays = "30d";
    public const string All = "all";

    public static bool TryNormalize(string? value, out string period)
    {
        period = string.IsNullOrWhiteSpace(value) ? SevenDays : value.Trim().ToLowerInvariant();
        return period is Today or SevenDays or ThirtyDays or All;
    }

    public static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Inclusive start of the reporting window. All-time starts at the Unix epoch.</summary>
    public static DateTimeOffset Start(string period, DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        if (period == All) return DateTimeOffset.UnixEpoch;
        if (period == SevenDays) return utcNow.AddDays(-7);
        if (period == ThirtyDays) return utcNow.AddDays(-30);

        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var midnight = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(midnight)) midnight = midnight.AddHours(1);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}

/// <summary>OpenRouter list price in USD per one million tokens.</summary>
public sealed record TokenPrice(decimal? PromptPerMillion, decimal? CompletionPerMillion);

public interface IModelPriceLookup
{
    Task<TokenPrice?> GetOpenRouterPriceAsync(string modelId, CancellationToken cancellationToken);
}

public sealed record ModelUsageDraft(
    Guid OwnerId,
    string Provider,
    string Purpose,
    string? Model,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningOutputTokens,
    decimal? EstimatedCostUsd,
    int DurationMs,
    string Outcome,
    int WebSearchActions);

public interface IModelUsageRecorder
{
    Task RecordAsync(ModelUsageDraft draft, CancellationToken cancellationToken);
}

public sealed record CountWindow(int InPeriod, int Total);

public sealed record PersonalizationSnapshot(
    int Score,
    string Band,
    string Summary,
    int ActiveMemories,
    int PinnedMemories,
    int SupersededMemories,
    int MemoryKinds,
    int PersonaTraits,
    bool HasCustomInstructions,
    bool HasPreferredName,
    int FeedbackRatings);

public sealed record UsageActivity(
    CountWindow MessagesSent,
    CountWindow AssistantReplies,
    CountWindow Conversations,
    CountWindow Dreams,
    CountWindow Memories,
    CountWindow Skills,
    CountWindow Files,
    CountWindow GraphEntities,
    CountWindow GraphRelations,
    CountWindow TasksCompleted,
    CountWindow Reminders,
    CountWindow Approvals,
    CountWindow WebSearches,
    CountWindow ChannelMessages,
    CountWindow FeedbackRatings,
    CountWindow BrowserSessions,
    DateTimeOffset? LastDreamAt,
    int DiaryEntries);

public sealed record PurposeCount(string Purpose, int Calls);

public sealed record ProviderUsage(
    int Calls,
    int CompletedCalls,
    int FailedCalls,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningOutputTokens,
    long TotalTokens,
    decimal? EstimatedCostUsd,
    int UnpricedCalls,
    int WebSearchActions,
    int AverageDurationMs,
    string CostNote,
    int LifetimeCalls,
    long LifetimeTotalTokens,
    decimal? LifetimeEstimatedCostUsd,
    IReadOnlyList<PurposeCount> Purposes);

public sealed record DailyUsagePoint(DateOnly Day, long CodexTokens, long OpenRouterTokens, decimal OpenRouterCostUsd);

public sealed record ModelUsageRow(
    string Provider,
    string Model,
    string Purpose,
    int Calls,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    decimal? EstimatedCostUsd);

public sealed record UsageDashboard(
    string Period,
    DateTimeOffset From,
    DateTimeOffset To,
    string TimeZoneId,
    DateTimeOffset GeneratedAt,
    PersonalizationSnapshot Personalization,
    UsageActivity Activity,
    ProviderUsage Codex,
    ProviderUsage OpenRouter,
    ProviderUsage? Other,
    IReadOnlyList<DailyUsagePoint> Daily,
    string? ChartNote,
    IReadOnlyList<ModelUsageRow> Models);

public interface IUsageDashboard
{
    Task<UsageDashboard> GetAsync(Guid ownerId, string period, CancellationToken cancellationToken);
}
