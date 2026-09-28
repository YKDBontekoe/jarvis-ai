using System.Text.RegularExpressions;

namespace Jarvis.Agents;

public static partial class VoiceSpeech
{
    public const string Prompt =
        "You are Jarvis speaking in realtime. Answer the user directly. Use the connected " +
        "Jarvis tools for memory, reminders, files, tasks, watches, MCP servers, and other " +
        "actions, and use live web search for current facts. Keep replies short and spoken. " +
        "Do not wait for a second chat conversion. Keep listening after each reply and allow " +
        "the user to interrupt by speaking.";

    public static string? SanitizeVoice(string? value)
    {
        if (value is null) return null;
        var voice = value.Trim().ToLowerInvariant();
        return voice.Length == 0 || !VoiceIdPattern().IsMatch(voice) ? null : voice;
    }

    public static string? ResolveVoice(string? metadataVoice, string? envVoice) =>
        SanitizeVoice(metadataVoice) ?? SanitizeVoice(envVoice);

    public static bool IsEcho(string heard, string spoken)
    {
        var heardNorm = Words(heard);
        var spokenNorm = Words(spoken);
        return heardNorm.Length >= 12 && spokenNorm.Length > 0 && spokenNorm.Contains(heardNorm, StringComparison.Ordinal);
    }

    private static string Words(string text) =>
        string.Join(' ', WordPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value));

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex VoiceIdPattern();

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordPattern();
}

public sealed class VoiceBargeIn(double holdSeconds = 0.45)
{
    private double? _armedAt;

    public void Reset() => _armedAt = null;

    public bool Consider(string partial, string spoken, bool assistantBusy, double now)
    {
        var text = partial.Trim();
        if (!assistantBusy || text.Length < 12 || VoiceSpeech.IsEcho(text, spoken))
        {
            _armedAt = null;
            return false;
        }

        if (_armedAt is null)
        {
            _armedAt = now;
            return false;
        }

        return now - _armedAt >= holdSeconds;
    }
}

public sealed class VoicePlaybackGate
{
    public int Generation { get; private set; }
    public bool OutputAllowed { get; private set; }
    public bool SuppressSpeak { get; private set; }

    public void Duck(bool suppressInflight = false)
    {
        OutputAllowed = false;
        Generation++;
        if (suppressInflight) SuppressSpeak = true;
    }

    public void BeginTurn() => SuppressSpeak = false;

    public bool AllowRealtimeOutput()
    {
        if (SuppressSpeak) return false;
        OutputAllowed = true;
        return true;
    }

    public bool TryStartSpeak()
    {
        if (SuppressSpeak) return false;
        OutputAllowed = true;
        if (SuppressSpeak)
        {
            OutputAllowed = false;
            return false;
        }

        return true;
    }

    public void FinishSpeak()
    {
        if (SuppressSpeak) OutputAllowed = false;
    }

    public int? AcceptFrame() => OutputAllowed ? Generation : null;
}
