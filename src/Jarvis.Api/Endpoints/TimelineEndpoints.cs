using System.Globalization;
using Jarvis.Application.Conversations;
using Jarvis.Application.Timeline;

namespace Jarvis.Api.Endpoints;

public sealed record TimelineEventDto(string Id, string Kind, string Title, string? Detail, DateTimeOffset At,
    DateOnly Date, Guid? TargetId, decimal? Amount, string? Currency);

public sealed record TimelineDayDto(DateOnly Date, IReadOnlyList<TimelineEventDto> Events);

public sealed record TimelineResponse(DateOnly From, DateOnly To, IReadOnlyList<TimelineDayDto> Days, int Total,
    bool Truncated, IReadOnlyList<string> FailedKinds);

public sealed record TimelineYearDto(int Year, DateOnly Date, IReadOnlyList<TimelineEventDto> Events);

public sealed record TimelineInsightsResponse(DateOnly From, DateOnly To, int DaysWithData,
    IReadOnlyList<TimelineInsight> Insights, IReadOnlyList<DayMetrics> Days);

/// <summary>
/// The life timeline: one chronological view over journal, spending, habits, people, tasks, reminders, memories,
/// and chats. It only reads, so nothing here writes to the audit log.
/// </summary>
internal static class TimelineEndpoints
{
    public static RouteGroupBuilder MapTimelineEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/timeline");

        group.MapGet("", async (string? from, string? to, string? kinds, string? q, int? limit,
            ITimelineService timeline, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await timeline.TodayAsync(currentUser.OwnerId, ct);
            if (!TryDate(to, today, out var end)) return EndpointHelpers.Invalid("to", "Use the date format YYYY-MM-DD.");
            if (!TryDate(from, end.AddDays(-29), out var start))
                return EndpointHelpers.Invalid("from", "Use the date format YYYY-MM-DD.");
            if (!TryKinds(kinds, out var kindSet))
                return EndpointHelpers.Invalid("kinds", "Unknown kind. Use " + string.Join(", ", TimelineKinds.All) + ".");

            var result = await timeline.QueryAsync(currentUser.OwnerId,
                new TimelineQuery(start, end, kindSet, q, limit ?? TimelineRules.DefaultLimit), ct);
            return Results.Ok(new TimelineResponse(result.From, result.To,
                result.Days.Select(day => new TimelineDayDto(day.Date, day.Events.Select(ToDto).ToArray())).ToArray(),
                result.Total, result.Truncated, result.Sources.Where(x => !x.Succeeded).Select(x => x.Kind).ToArray()));
        }).WithName("QueryTimeline");

        group.MapGet("/on-this-day", async (int? years, ITimelineService timeline, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await timeline.TodayAsync(currentUser.OwnerId, ct);
            var result = await timeline.OnThisDayAsync(currentUser.OwnerId, today,
                years ?? TimelineRules.OnThisDayYears, ct);
            return Results.Ok(result.Select(x => new TimelineYearDto(x.Year, x.Date, x.Events.Select(ToDto).ToArray()))
                .ToArray());
        }).WithName("TimelineOnThisDay");

        group.MapGet("/insights", async (int? days, ITimelineService timeline, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await timeline.TodayAsync(currentUser.OwnerId, ct);
            var result = await timeline.InsightsAsync(currentUser.OwnerId, today,
                days ?? TimelineRules.DefaultInsightDays, ct);
            return Results.Ok(new TimelineInsightsResponse(result.From, result.To, result.DaysWithData,
                result.Insights, result.Days));
        }).WithName("TimelineInsights");

        return api;
    }

    private static TimelineEventDto ToDto(TimelineEvent x) =>
        new(x.Id, x.Kind, x.Title, x.Detail, x.At, x.Date, x.TargetId, x.Amount, x.Currency);

    private static bool TryDate(string? text, DateOnly fallback, out DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            date = fallback;
            return true;
        }
        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out date);
    }

    private static bool TryKinds(string? text, out IReadOnlySet<string>? kinds)
    {
        kinds = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant()).Distinct().ToArray();
        if (parts.Any(x => !TimelineKinds.IsValid(x))) return false;
        kinds = parts.ToHashSet(StringComparer.Ordinal);
        return true;
    }
}
