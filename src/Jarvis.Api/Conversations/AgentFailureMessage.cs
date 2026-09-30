namespace Jarvis.Api.Conversations;

/// <summary>
/// Turns an agent-run exception into a message the owner can act on. It classifies the failure and returns a
/// fixed sentence, so raw exception text (which can name hosts, request ids, or paths) is never shown.
/// </summary>
internal static class AgentFailureMessage
{
    public static string For(Exception exception)
    {
        var text = Flatten(exception);
        if (Has(text, "401", "unauthorized", "missing bearer", "not logged in", "log in again", "login required",
                "token_expired", "refresh token"))
            return "Codex is signed out on the Jarvis server. Sign in again with “codex login” there, then retry.";
        if (Has(text, "429", "rate limit", "usage limit", "quota", "too many requests"))
            return "Your Codex plan has reached its usage limit. Try again later, or switch models in Settings → Models.";
        if (Has(text, "could not start the codex", "codex executable", "no such file or directory") &&
            Has(text, "codex"))
            return "The Codex CLI isn’t available on the Jarvis server. Check Settings → Models.";
        if (exception is TimeoutException || Has(text, "timed out", "timeout"))
            return "The model took too long to answer. Try again in a moment.";
        return ConversationTurnService.FailureMessage;
    }

    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add(current.GetType().Name + " " + current.Message);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    private static bool Has(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.Ordinal));
}
