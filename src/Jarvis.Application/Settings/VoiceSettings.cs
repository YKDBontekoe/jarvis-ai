namespace Jarvis.Application.Settings;

public sealed record VoiceSettings(bool HandsFree = true, bool Captions = true, string? Voice = null)
{
    public static VoiceSettings Default { get; } = new();
}

/// <summary>
/// Picks a voice the installed Codex CLI reported. An empty catalog leaves the choice to that CLI.
/// </summary>
public static class VoiceSelection
{
    public static string? Resolve(string? selected, IEnumerable<string> supported, string? cliDefault)
    {
        var voices = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var voice in supported)
        {
            var id = voice.Trim().ToLowerInvariant();
            if (id.Length == 0 || !seen.Add(id)) continue;
            voices.Add(id);
        }
        var chosen = Token(selected);
        if (voices.Count == 0) return chosen;
        if (chosen is not null && seen.Contains(chosen)) return chosen;
        var fallback = Token(cliDefault);
        if (fallback is not null && seen.Contains(fallback)) return fallback;
        return voices[0];
    }

    private static string? Token(string? value)
    {
        var voice = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(voice) ? null : voice;
    }
}
