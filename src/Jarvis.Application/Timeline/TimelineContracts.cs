namespace Jarvis.Application.Timeline;

/// <summary>The kinds of moments the life timeline merges into one chronological list.</summary>
public static class TimelineKinds
{
    public const string Journal = "journal";
    public const string Expense = "expense";
    public const string Habit = "habit";
    public const string Contact = "contact";
    public const string Birthday = "birthday";
    public const string Task = "task";
    public const string Reminder = "reminder";
    public const string Memory = "memory";
    public const string Conversation = "conversation";
    public const string Decision = "decision";

    public static readonly IReadOnlyList<string> All =
        [Journal, Expense, Habit, Contact, Birthday, Task, Reminder, Memory, Conversation, Decision];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? kind) =>
        kind is not null && Known.Contains(kind);
}

/// <summary>
/// One moment in the owner's life. <see cref="Date"/> is the owner's local calendar day; <see cref="TargetId"/> is
/// the row the moment came from so a client can open it. <see cref="Amount"/> is only set for expenses.
/// </summary>
public sealed record TimelineEvent(
    string Id,
    string Kind,
    string Title,
    string? Detail,
    DateTimeOffset At,
    DateOnly Date,
    Guid? TargetId = null,
    decimal? Amount = null,
    string? Currency = null);

public static class TimelineRules
{
    public const int MaxWindowDays = 400;
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;
    public const int MaxDetailLength = 200;
    public const int MaxTitleLength = 120;
    public const int OnThisDayYears = 5;
    public const int DefaultInsightDays = 60;
    public const int MaxInsightDays = 180;

    /// <summary>The local calendar day of a moment in the owner's time zone.</summary>
    public static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    /// <summary>
    /// A moment inside <paramref name="date"/>: the creation time when it fell on that local day, else noon. Rows
    /// that only know their day (an expense, a journal entry) still sort sensibly with rows that know the time.
    /// </summary>
    public static DateTimeOffset Stamp(DateOnly date, DateTimeOffset createdAt, TimeZoneInfo zone)
    {
        return LocalDate(createdAt, zone) == date ? createdAt : Noon(date, zone);
    }

    /// <summary>Noon on <paramref name="date"/> in the owner's time zone.</summary>
    public static DateTimeOffset Noon(DateOnly date, TimeZoneInfo zone)
    {
        var noon = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(noon, zone.GetUtcOffset(noon)).ToUniversalTime();
    }

    /// <summary>Collapses whitespace and cuts the text at <paramref name="max"/> characters with an ellipsis.</summary>
    public static string Shorten(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }
}

/// <summary>The window a source reads: inclusive local dates and the owner's time zone.</summary>
public sealed record TimelineWindow(Guid OwnerId, DateOnly From, DateOnly To, TimeZoneInfo Zone, int Limit);

/// <summary>Reads one area of the owner's data as timeline moments. Sources never write.</summary>
public interface ITimelineSource
{
    /// <summary>The kinds this source can produce, so a filtered query skips sources it does not need.</summary>
    IReadOnlySet<string> Kinds { get; }

    Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window, CancellationToken cancellationToken);
}

public sealed record TimelineQuery(
    DateOnly From,
    DateOnly To,
    IReadOnlySet<string>? Kinds = null,
    string? Text = null,
    int Limit = TimelineRules.DefaultLimit);

public sealed record TimelineDay(DateOnly Date, IReadOnlyList<TimelineEvent> Events);

public sealed record TimelineSourceStatus(string Kind, bool Succeeded);

public sealed record TimelineResult(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TimelineDay> Days,
    int Total,
    bool Truncated,
    IReadOnlyList<TimelineSourceStatus> Sources);

/// <summary>Moments that happened on this calendar day in an earlier year.</summary>
public sealed record TimelineYear(int Year, DateOnly Date, IReadOnlyList<TimelineEvent> Events);

/// <summary>One finding about how areas of the owner's life move together.</summary>
public sealed record TimelineInsight(
    string Id,
    string Headline,
    string Detail,
    double Strength,
    IReadOnlyList<string> Metrics);

/// <summary>What the owner's day looked like in numbers. Null means "not recorded that day".</summary>
public sealed record DayMetrics(
    DateOnly Date,
    double? Mood,
    double? Energy,
    double? Stress,
    double? Rating,
    decimal Spend,
    double? HabitRatio,
    int TasksCompleted);

public sealed record TimelineInsights(
    DateOnly From,
    DateOnly To,
    int DaysWithData,
    IReadOnlyList<TimelineInsight> Insights,
    IReadOnlyList<DayMetrics> Days);

public interface ITimelineService
{
    Task<TimelineResult> QueryAsync(Guid ownerId, TimelineQuery query, CancellationToken cancellationToken);

    /// <summary>Earlier years' moments on today's month and day, newest year first. Years without any are left out.</summary>
    Task<IReadOnlyList<TimelineYear>> OnThisDayAsync(Guid ownerId, DateOnly today, int yearsBack,
        CancellationToken cancellationToken);

    Task<TimelineInsights> InsightsAsync(Guid ownerId, DateOnly today, int days, CancellationToken cancellationToken);

    /// <summary>Today in the owner's configured time zone.</summary>
    Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken);
}
