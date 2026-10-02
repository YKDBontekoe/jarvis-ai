using System.Diagnostics;
using System.Globalization;
using Jarvis.Application.Usage;

namespace Jarvis.Agents.Telemetry;

/// <summary>
/// OpenTelemetry GenAI attributes Sentry's agent view reads. Values are model, token, and cost figures only.
/// </summary>
public static class GenAiTelemetry
{
    public const string SourceName = "Jarvis.ModelUsage";

    public const string OperationName = "gen_ai.operation.name";
    public const string AgentName = "gen_ai.agent.name";
    public const string ConversationId = "gen_ai.conversation.id";
    public const string ProviderName = "gen_ai.provider.name";
    public const string RequestModel = "gen_ai.request.model";
    public const string ToolName = "gen_ai.tool.name";
    public const string InputTokens = "gen_ai.usage.input_tokens";
    public const string OutputTokens = "gen_ai.usage.output_tokens";
    public const string CacheReadInputTokens = "gen_ai.usage.cache_read.input_tokens";
    public const string ReasoningOutputTokens = "gen_ai.usage.reasoning.output_tokens";
    public const string CostInput = "gen_ai.cost.input_tokens";
    public const string CostOutput = "gen_ai.cost.output_tokens";
    public const string CostTotal = "gen_ai.cost.total_tokens";

    public static readonly ActivitySource Source = new(SourceName);

    public static void TagInvokeAgent(Activity? activity, Guid conversationId)
    {
        if (activity is null) return;
        activity.SetTag(OperationName, "invoke_agent");
        activity.SetTag(AgentName, "Jarvis");
        activity.SetTag(ConversationId, conversationId.ToString("D"));
    }

    public static void TagTool(Activity? activity, string toolName)
    {
        if (activity is null) return;
        activity.SetTag(OperationName, "execute_tool");
        activity.SetTag(ToolName, toolName);
    }

    public static void TagChat(Activity? activity, string provider, string? model)
    {
        if (activity is null) return;
        activity.SetTag(OperationName, "chat");
        activity.SetTag(ProviderName, provider);
        if (!string.IsNullOrWhiteSpace(model))
            activity.SetTag(RequestModel, model);
    }

    public static void TagUsage(Activity? activity, long inputTokens, long outputTokens, long cachedInputTokens,
        long reasoningOutputTokens)
    {
        if (activity is null) return;
        activity.SetTag(InputTokens, inputTokens);
        activity.SetTag(OutputTokens, outputTokens);
        activity.SetTag(CacheReadInputTokens, cachedInputTokens);
        activity.SetTag(ReasoningOutputTokens, reasoningOutputTokens);
    }

    public static void TagEmbeddings(Activity? activity, string model)
    {
        if (activity is null) return;
        activity.SetTag(OperationName, "embeddings");
        activity.SetTag(RequestModel, model);
    }

    public static void TagCost(Activity? activity, TokenCost cost)
    {
        if (activity is null) return;
        activity.SetTag(CostInput, DecimalTag(cost.Input));
        activity.SetTag(CostOutput, DecimalTag(cost.Output));
        activity.SetTag(CostTotal, DecimalTag(cost.Total));
    }

    private static string DecimalTag(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>USD portions of one model call, matching Sentry's <c>gen_ai.cost.*</c> attributes.</summary>
public readonly record struct TokenCost(decimal Input, decimal Output, decimal Total);
