using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

public static class VoiceTools
{
    public const string ApprovalRequestPrefix = "voice-";
    public const string ApprovalNeeded = "I need your approval before I can continue. Check the Jarvis app.";
    public const string VoiceRealtimeAppendix = """
        You are speaking with the user in realtime voice. Keep answers short and easy to say out loud.
        The connected Jarvis tools are the same tools as chat: memory, reminders, files, tasks, watches, MCP servers, devices, browser, and UI cards. Call them yourself during this voice session; do not wait for a separate chat conversion.
        Never invent remembered facts — call ListMemories or SearchMemory. Use live Codex web search for current events and include today's UTC date from the time reference in the query.
        When a tool says approval is required, tell the user to check the Jarvis app, then keep listening.
        When an MCP server needs authorization, call RequestMcpAuthorization and AskForMcpCredential in the app rather than asking them to dictate a token.
        """;

    public static bool IsVoiceApproval(string requestId) =>
        requestId.StartsWith(ApprovalRequestPrefix, StringComparison.Ordinal);

    public static string NewApprovalRequestId() => ApprovalRequestPrefix + Guid.CreateVersion7().ToString("N");

    public static bool RequiresApproval(AIFunction function)
    {
        for (var current = function; current is not null;)
        {
            if (current is ApprovalRequiredAIFunction) return true;
            if (InnerFunction(current) is not { } inner || ReferenceEquals(inner, current)) return false;
            current = inner;
        }

        return false;
    }

    public static AIFunction UnwrapApprovals(AIFunction function)
    {
        for (var current = function; current is not null;)
        {
            if (current is not ApprovalRequiredAIFunction) return current;
            if (InnerFunction(current) is not { } inner || ReferenceEquals(inner, current)) return current;
            current = inner;
        }

        return function;
    }

    public static VoiceToolDescriptor Describe(AIFunction function) =>
        new(function.Name, function.Description ?? "", function.JsonSchema.Clone(), RequiresApproval(function));

    public static AIFunctionArguments ToArguments(string? json)
    {
        var parsed = Jarvis.Application.Conversations.ToolCallArguments.Parse(json);
        var arguments = new AIFunctionArguments();
        foreach (var pair in parsed)
            arguments[pair.Key] = pair.Value;
        return arguments;
    }

    public static string FormatResult(object? result)
    {
        return result switch
        {
            null => "",
            string text => text,
            JsonElement json => json.ValueKind == JsonValueKind.String ? json.GetString() ?? "" : json.GetRawText(),
            _ => result.ToString() ?? ""
        };
    }

    private static AIFunction? InnerFunction(AIFunction function)
    {
        for (var type = function.GetType(); type is not null && type != typeof(AIFunction); type = type.BaseType)
        {
            var property = type.GetProperty("InnerFunction",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null) return property.GetValue(function) as AIFunction;
        }

        return null;
    }
}

public sealed record VoiceToolDescriptor(string Name, string Description, JsonElement InputSchema, bool RequiresApproval);
