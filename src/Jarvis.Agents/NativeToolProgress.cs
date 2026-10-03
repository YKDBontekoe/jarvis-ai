using Jarvis.Application.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>
/// Progress for work the model provider does itself, such as Codex's native web search, shell commands and
/// file edits, so the
/// client can show it like any other tool step. It travels in update metadata rather than as
/// function call content, so it never enters the stored transcript or the next prompt. Only the
/// fixed tool name, an opaque id, and the phase are carried; never queries, commands or results.
/// </summary>
internal static class NativeToolProgress
{
    public const string WebSearch = "WebSearch";
    public const string RunCommand = "RunCommand";
    public const string EditFiles = "EditFiles";
    private const string CallIdKey = "jarvis.native_tool.call_id";
    private const string NameKey = "jarvis.native_tool.name";
    private const string PhaseKey = "jarvis.native_tool.phase";

    public static ChatResponseUpdate Create(string callId, string toolName, string phase) => new()
    {
        Role = ChatRole.Assistant,
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [CallIdKey] = callId,
            [NameKey] = toolName,
            [PhaseKey] = phase
        }
    };

    public static AgentToolProgress? Read(AgentResponseUpdate update)
    {
        var properties = update.AdditionalProperties ??
                         (update.RawRepresentation as ChatResponseUpdate)?.AdditionalProperties;
        if (properties is null ||
            !properties.TryGetValue(CallIdKey, out var callId) || callId is not string id ||
            !properties.TryGetValue(NameKey, out var name) || name is not string toolName ||
            !properties.TryGetValue(PhaseKey, out var phase) || phase is not ("started" or "completed"))
            return null;
        return new AgentToolProgress(id, toolName, (string)phase);
    }
}
