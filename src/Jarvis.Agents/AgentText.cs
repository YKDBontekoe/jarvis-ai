using System.Globalization;

namespace Jarvis.Agents;

internal static class AgentText
{
    public static string Time(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset? value) => value is { } time ? Time(time) : "never";

    public static string Limit(string? value, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 1)] + "…";
    }
}
