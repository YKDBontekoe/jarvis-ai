using Jarvis.Application.Events;

namespace Jarvis.Application.Proactivity;

public static class ProactiveAttentionRules
{
    public static bool Covered(IReadOnlyList<ProactiveCase> cases, string reference)
    {
        if (!EntityRef.TryParse(reference, out var entity)) return false;
        var canonical = entity.ToString();
        return cases.Any(c => !c.Terminal && c.Enabled && c.Status is not ("paused" or "needs_input" or "suggested") &&
                              (c.RootRef == canonical || c.SourceRefs.Contains(canonical)));
    }

    public static string? HeartbeatReference(string key)
    {
        var separator = key.IndexOf(':');
        if (separator <= 0) return null;
        var kind = key[..separator] switch { "commitment" => "commitment", "inbox" => "inbox_thread", _ => null };
        return kind is not null && Guid.TryParse(key[(separator + 1)..], out var id) ? $"{kind}:{id}" : null;
    }
}
