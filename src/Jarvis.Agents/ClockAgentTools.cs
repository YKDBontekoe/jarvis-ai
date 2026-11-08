using System.ComponentModel;
using System.Globalization;
using Jarvis.Application.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

internal sealed class ClockAgentTools(TimeProvider clock)
{
    [Description("Get the exact current date and time in an IANA time zone such as Europe/Amsterdam or America/New_York. Use this for time-zone conversions and before scheduling anything relative to a local time the user mentioned.")]
    public string GetCurrentTime(
        [Description("An IANA time zone identifier. Use UTC when unknown.")] string timeZoneId = "UTC")
    {
        if (!TryFindTimeZone(timeZoneId, out var zone))
            return $"'{AgentText.Limit(timeZoneId, 80)}' is not a known IANA time zone. Ask the user for their city or time zone.";
        return Describe(clock.GetUtcNow(), zone);
    }

    /// <summary>
    /// Without <paramref name="includeSeconds"/> the text only changes once a minute, which keeps the per-turn context
    /// stable enough for the model's prompt cache.
    /// </summary>
    internal static string Describe(DateTimeOffset utcNow, TimeZoneInfo zone, bool includeSeconds = true)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var (display, iso) = includeSeconds
            ? ("dddd yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:sszzz")
            : ("dddd yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mmzzz");
        return $"{zone.Id}: {local.ToString(display, CultureInfo.InvariantCulture)} " +
               $"(ISO {local.ToString(iso, CultureInfo.InvariantCulture)}, UTC offset {FormatOffset(local.Offset)})";
    }

    internal static bool TryFindTimeZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        var id = timeZoneId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Z", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.Length > 64) return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// Supplies the current time on every turn, including the owner's local time zone so relative dates resolve without
/// asking: the home zone they chose in Settings, else their briefing's zone (the same order reminders use).
/// </summary>
internal sealed class ClockContextProvider(Jarvis.Application.Settings.IOwnerSettingsStore? settings,
    IDailyBriefingRepository briefings, Guid ownerId, TimeProvider clock)
    : MessageAIContextProvider
{
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var text = "Current time reference: " + ClockAgentTools.Describe(now, TimeZoneInfo.Utc, includeSeconds: false) + ".";
        // Only the briefing's zone used to be read, so a home zone chosen in Settings was ignored and Jarvis asked.
        var home = await Jarvis.Application.Preferences.OwnerZoneResolver.ResolveAsync(settings, briefings, ownerId,
            cancellationToken);
        if (home.Source != Jarvis.Application.Preferences.ZoneSources.Default && home.Zone != TimeZoneInfo.Utc)
            text += " The user's configured local time zone is " + ClockAgentTools.Describe(now, home.Zone, includeSeconds: false) + ".";
        else
            text += " The user's local time zone is not configured; ask for it when a local time matters.";
        return [new ChatMessage(ChatRole.User, text)];
    }
}
