namespace Jarvis.Agents;

internal static class AgentText
{
    public static string Limit(string? value, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 1)] + "…";
    }
}
