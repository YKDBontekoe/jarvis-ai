namespace Jarvis.Application.Settings;

public sealed record VoiceSettings(bool HandsFree = true, bool Captions = true)
{
    public static VoiceSettings Default { get; } = new();
}
