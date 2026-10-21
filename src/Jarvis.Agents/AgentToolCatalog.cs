namespace Jarvis.Agents;

/// <summary>
/// The tool names the agent built in this scope can call, filled in by <see cref="JarvisAgentFactory"/> once every
/// contributor has added its tools. Skills read it to tell whether the tools they rely on are here. Before the agent
/// is built nothing is known, and every tool counts as available.
/// </summary>
public sealed class AgentToolCatalog
{
    private IReadOnlySet<string>? _names;

    public bool IsKnown => _names is not null;

    public void Set(IEnumerable<string> names) => _names = names.ToHashSet(StringComparer.Ordinal);

    public bool Contains(string name) => _names is null || _names.Contains(name);

    /// <summary>The names in <paramref name="required"/> this agent cannot call, in their original order.</summary>
    public IReadOnlyList<string> Missing(IEnumerable<string> required) =>
        _names is null ? [] : required.Where(name => !_names.Contains(name)).ToArray();
}
