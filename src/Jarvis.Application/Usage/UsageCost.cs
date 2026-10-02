namespace Jarvis.Application.Usage;

public static class UsageCost
{
    /// <summary>
    /// Estimates USD from OpenRouter's per-million token prices. Cached input is billed at the prompt
    /// price because those tokens are already included in the input count.
    /// </summary>
    public static decimal? Estimate(long inputTokens, long outputTokens, TokenPrice? price)
    {
        var split = Split(inputTokens, outputTokens, price);
        return split?.Total;
    }

    /// <summary>Input, output, and total USD for a call. Null when the catalog has no price.</summary>
    public static (decimal Input, decimal Output, decimal Total)? Split(long inputTokens, long outputTokens,
        TokenPrice? price)
    {
        if (inputTokens < 0 || outputTokens < 0) return null;
        if (price is null || (price.PromptPerMillion is null && price.CompletionPerMillion is null)) return null;
        var input = Round(inputTokens / 1_000_000m * (price.PromptPerMillion ?? 0m));
        var output = Round(outputTokens / 1_000_000m * (price.CompletionPerMillion ?? 0m));
        return (input, output, input + output);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 8, MidpointRounding.AwayFromZero);
}
