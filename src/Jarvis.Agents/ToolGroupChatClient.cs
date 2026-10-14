using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>
/// Sits between function invocation and the model. Function invocation still sees every tool, so execution and
/// approval gating are unchanged; the model is only shown the always-on tools plus the <see cref="ToolGroups"/> the
/// conversation needs right now. The full name list travels in <see cref="ToolGroups.CallableToolsProperty"/> so a
/// model client that validates names accepts a call to a tool that is callable but not offered yet.
/// </summary>
internal sealed class ToolGroupChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private static readonly Meter Meter = new("Jarvis.ToolGroups");
    private static readonly Histogram<int> OfferedTools = Meter.CreateHistogram<int>(
        "jarvis.agent.tools.offered", "{tool}", "Jarvis functions shown to the model on one call.");
    private static readonly Histogram<int> DeferredTools = Meter.CreateHistogram<int>(
        "jarvis.agent.tools.deferred", "{tool}", "Jarvis functions left out of one model call until a turn needs them.");

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        return base.GetResponseAsync(list, Narrow(list, options), cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        await foreach (var update in base.GetStreamingResponseAsync(list, Narrow(list, options), cancellationToken))
            yield return update;
    }

    internal static ChatOptions? Narrow(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools) return options;
        if (!tools.Any(tool => tool.Name == ToolGroups.LoadToolsName)) return options;

        var active = ToolGroups.ActiveGroups(messages);
        var offered = new List<AITool>(tools.Count);
        var deferred = 0;
        foreach (var tool in tools)
        {
            if (ToolGroups.ForTool(tool.Name) is { } group && !active.Contains(group.Id))
            {
                deferred++;
                continue;
            }
            offered.Add(tool);
        }
        OfferedTools.Record(offered.Count);
        DeferredTools.Record(deferred);
        if (deferred == 0) return options;

        var narrowed = options.Clone();
        narrowed.Tools = offered;
        narrowed.AdditionalProperties ??= [];
        narrowed.AdditionalProperties[ToolGroups.CallableToolsProperty] =
            tools.Select(tool => tool.Name).ToArray();
        return narrowed;
    }
}
