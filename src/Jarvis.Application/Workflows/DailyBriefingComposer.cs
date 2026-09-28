using System.Globalization;
using Jarvis.Domain.Workflows;

namespace Jarvis.Application.Workflows;

public sealed record DailyBriefingItem(string Title, string Detail);

public sealed record DailyBriefingFacts(
    DateOnly LocalDate,
    string TimeZoneId,
    IReadOnlyList<DailyBriefingItem> Reminders,
    IReadOnlyList<DailyBriefingItem> Tasks);

public interface IDailyBriefingNarrator
{
    Task<string?> NarrateAsync(Guid ownerId, DailyBriefingFacts facts, CancellationToken cancellationToken);
}

public sealed class NoOpDailyBriefingNarrator : IDailyBriefingNarrator
{
    public Task<string?> NarrateAsync(Guid ownerId, DailyBriefingFacts facts, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}

public static class DailyBriefingComposer
{
    public const int MaxBodyLength = 2_000;

    public static string Compose(DailyBriefingFacts facts)
    {
        var lines = new List<string>();
        if (facts.Reminders.Count == 0) lines.Add("No reminders are due today.");
        else
        {
            lines.Add("Today's reminders:");
            lines.AddRange(facts.Reminders.Select(item => $"• {item.Detail} — {item.Title}"));
        }

        if (facts.Tasks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Active tasks:");
            lines.AddRange(facts.Tasks.Select(item => $"• {item.Title} ({item.Detail})"));
        }

        return Bound(string.Join('\n', lines));
    }

    public static string Combine(string? narration, string facts)
    {
        if (string.IsNullOrWhiteSpace(narration)) return facts;
        var intro = narration.Trim();
        if (intro.Length > 600) intro = intro[..597] + "…";
        return Bound(intro + "\n\n" + facts);
    }

    public static DailyBriefingItem ReminderItem(ReminderRecord reminder, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(reminder.DueAt, timeZone);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var recurrence = ReminderSchedule.Describe(reminder);
        var detail = string.IsNullOrEmpty(recurrence) ? time : $"{time} ({recurrence})";
        return new DailyBriefingItem(reminder.Title, detail);
    }

    public static DailyBriefingItem TaskItem(JarvisTaskRecord task) =>
        new(task.Title, task.Status.Replace('_', ' '));

    private static string Bound(string body) =>
        body.Length > MaxBodyLength ? body[..(MaxBodyLength - 3)] + "…" : body;
}
