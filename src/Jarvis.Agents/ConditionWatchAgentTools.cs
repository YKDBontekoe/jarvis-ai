using System.ComponentModel;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;

namespace Jarvis.Agents;

internal sealed class ConditionWatchAgentTools(IConditionWatchService watches, ICurrentUser currentUser)
{
    [Description("Create a durable watch for a public JSON API endpoint. The endpoint must be HTTPS, credential-free, and return an object with a numeric value at the requested dot-separated property path. Jarvis checks it on a timer and sends a notification when the value reaches the threshold. Never use private, local, authenticated, or secret-bearing URLs.")]
    public async Task<string> CreateConditionWatchAsync(
        [Description("A short user-facing name for this watch.")] string title,
        [Description("An unauthenticated public HTTPS JSON URL. Do not include private network addresses, user credentials, API keys, or other secrets.")] string url,
        [Description("A dot-separated object property path to a numeric JSON value, such as data.price.")] string jsonPath,
        [Description("Use below to alert when the value is at or below the threshold, or above to alert when it is at or above the threshold.")] string comparison,
        [Description("The numeric trigger value.")] double threshold,
        [Description("Minutes between deterministic checks, from 5 through 1,440.")] int intervalMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var watch = await watches.CreateAsync(currentUser.OwnerId,
                new CreateConditionWatchRequest(title, url, jsonPath, comparison, threshold, intervalMinutes),
                cancellationToken);
            var symbol = watch.Comparison == "below" ? "≤" : "≥";
            return $"Condition watch created (id: {watch.Id:D}, status: {watch.Status}, alert when {watch.JsonPath} {symbol} {watch.Threshold} {watch.IntervalMinutes}-minute checks).";
        }
        catch (ArgumentException exception)
        {
            return $"I could not create that watch: {exception.Message}";
        }
    }
}
