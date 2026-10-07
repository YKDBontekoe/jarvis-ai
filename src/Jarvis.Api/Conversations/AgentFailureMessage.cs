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
        if (SignedOut(text))
            return "Codex is signed out on the Jarvis server. Sign in again with “codex login” there, then retry.";
        if (RateLimited(text))
            return "Your Codex plan has reached its usage limit. Try again later, or switch models in Settings → Models.";
        if (CliMissing(text))
            return "The Codex CLI isn’t available on the Jarvis server. Check Settings → Models.";
        if (exception is TimeoutException || Has(text, "timed out", "timeout"))
            return "The model took too long to answer. Try again in a moment.";
        return ConversationTurnService.FailureMessage;
    }

    private static bool SignedOut(string text) =>
        Has(text, "401", "unauthorized", "missing bearer", "not logged in", "log in again", "login required",
            "token_expired", "refresh token");

    private static bool RateLimited(string text) =>
        Has(text, "429", "rate limit", "usage limit", "quota", "too many requests");

    private static bool CliMissing(string text) =>
        Has(text, "could not start the codex", "codex executable", "no such file or directory") && Has(text, "codex");

    /// <summary>
    /// True for a failure that a second attempt can fix: the model process died or timed out, or the connection broke.
    /// Signed-out, rate-limited and missing-CLI failures would only fail the same way again.
    /// </summary>
    public static bool IsRetryable(Exception exception)
    {
        var text = Flatten(exception);
        if (SignedOut(text) || RateLimited(text) || CliMissing(text)) return false;
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is TimeoutException or IOException or HttpRequestException) return true;
        return Has(text, "exited unexpectedly", "returned no thread identifier", "non-prefix assistant response");
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
