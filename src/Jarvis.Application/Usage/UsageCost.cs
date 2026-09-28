namespace Jarvis.Application.Usage;

public static class UsageCost
{
    /// <summary>
    /// Estimates USD from OpenRouter's per-million token prices. Cached input is billed at the prompt
    /// price because those tokens are already included in the input count.
    /// </summary>
    public static decimal? Estimate(long inputTokens, long outputTokens, TokenPrice? price)
    {
        if (inputTokens < 0 || outputTokens < 0) return null;
        if (price is null || (price.PromptPerMillion is null && price.CompletionPerMillion is null)) return null;
        var cost = inputTokens / 1_000_000m * (price.PromptPerMillion ?? 0m)
            + outputTokens / 1_000_000m * (price.CompletionPerMillion ?? 0m);
        return decimal.Round(cost, 8, MidpointRounding.AwayFromZero);
    }
}
