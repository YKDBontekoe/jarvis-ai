using System.Globalization;
using Jarvis.Application.Preferences;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Modes;

/// <summary>When the "while you were away" summary may be sent. Null means as soon as the mode allows it.</summary>
public sealed record DigestPreferences(string? Time = null);

public static class DigestSchedule
{
    public static TimeOnly? Parse(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    /// <summary>
    /// With no time set the summary goes as soon as pushes are allowed again. With a time set it waits for that time,
    /// and goes at most once a day: not before the time today, and not again if one already went out today.
    /// </summary>
    public static bool IsDue(string? time, DateTimeOffset nowLocal, DateTimeOffset? lastSentLocal)
    {
        if (Parse(time) is not { } at) return true;
        if (TimeOnly.FromDateTime(nowLocal.DateTime) < at) return false;
        return lastSentLocal is null || DateOnly.FromDateTime(lastSentLocal.Value.DateTime) < DateOnly.FromDateTime(nowLocal.DateTime);
    }
}

public sealed record DigestTimeView(string? Time, string Explanation);

public interface IDigestScheduleService
{
    Task<DigestTimeView> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Saves a time as HH:mm, or clears it with null or an empty string. Null result: not a valid time.</summary>
    Task<DigestTimeView?> SetAsync(Guid ownerId, string? time, CancellationToken cancellationToken);
}

public sealed class DigestScheduleService(IOwnerSettingsStore settings) : IDigestScheduleService
{
    public async Task<DigestTimeView> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        View((await settings.GetAsync<DigestPreferences>(ownerId, SettingsSections.DigestTime, cancellationToken))?.Time);

    public async Task<DigestTimeView?> SetAsync(Guid ownerId, string? time, CancellationToken cancellationToken)
    {
        string? saved = null;
        if (!string.IsNullOrWhiteSpace(time))
        {
            if (DigestSchedule.Parse(time) is not { } parsed) return null;
            saved = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        await settings.SaveAsync(ownerId, SettingsSections.DigestTime, new DigestPreferences(saved), cancellationToken);
        return View(saved);
    }

    private static DigestTimeView View(string? time) => new(time, time is null
        ? "The summary of what a quiet mode held back is sent as soon as pushes are allowed again."
        : $"What a quiet mode held back is summed up once a day, at {time} in your home time zone, not as soon as the mode ends. Approvals and problems are never held for it.");
}
