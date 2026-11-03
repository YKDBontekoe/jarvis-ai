namespace Jarvis.Application.Proactivity;

/// <summary>Conservative capture only: this never creates a reminder, accepted promise, or enabled job.</summary>
public static class OpenLoopDetector
{
    public static ProactiveCaseDraft? Detect(string text, Guid messageId, Guid? profileId)
    {
        if (text.Length > 2000) return null;
        var clean = string.Join(' ', text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var lower = clean.ToLowerInvariant();
        var explicitRequest = new[] { "keep track of ", "track this: ", "follow up on ", "houd bij ", "houd dit bij: " }
            .Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal));
        var inferred = lower.StartsWith("i'm waiting for ", StringComparison.Ordinal) ||
            lower.StartsWith("i am waiting for ", StringComparison.Ordinal) || lower.StartsWith("ik wacht op ", StringComparison.Ordinal) ||
            lower.StartsWith("compare ", StringComparison.Ordinal) && lower.EndsWith(" later", StringComparison.Ordinal);
        if (!explicitRequest && !inferred) return null;
        var reference = $"message:{messageId}";
        return new("open_loop", clean[..Math.Min(clean.Length, 120)], clean, [reference], reference,
            profileId, Explicit: explicitRequest, Enabled: false);
    }
}
