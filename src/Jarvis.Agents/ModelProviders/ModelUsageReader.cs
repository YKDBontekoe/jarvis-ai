using Microsoft.Extensions.AI;

namespace Jarvis.Agents.ModelProviders;

internal readonly record struct ReadModelUsage(
    long InputTokens, long OutputTokens, long CachedInputTokens, long ReasoningOutputTokens, int WebSearchActions);

internal static class ModelUsageReader
{
    public static ReadModelUsage Read(UsageDetails? usage)
    {
        if (usage is null) return default;
        var input = NonNegative(usage.InputTokenCount);
        var output = NonNegative(usage.OutputTokenCount);
        if (input == 0 && output == 0) input = NonNegative(usage.TotalTokenCount);
        var cached = NonNegative(usage.CachedInputTokenCount);
        var reasoning = NonNegative(usage.ReasoningTokenCount);
        var webSearches = 0;
        if (usage.AdditionalCounts is not null)
        {
            foreach (var pair in usage.AdditionalCounts)
            {
                if (pair.Key.Equals("webSearchActions", StringComparison.OrdinalIgnoreCase))
                    webSearches += (int)Math.Clamp(pair.Value, 0, int.MaxValue);
                else if (cached == 0 && pair.Key.Contains("cached", StringComparison.OrdinalIgnoreCase))
                    cached += NonNegative(pair.Value);
                else if (reasoning == 0 && pair.Key.Contains("reasoning", StringComparison.OrdinalIgnoreCase))
                    reasoning += NonNegative(pair.Value);
            }
        }
        return new ReadModelUsage(input, output, cached, reasoning, webSearches);
    }

    private static long NonNegative(long? value) => value is > 0 ? value.Value : 0;
}
