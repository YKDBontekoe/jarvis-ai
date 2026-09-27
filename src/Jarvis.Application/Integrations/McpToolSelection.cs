using System.Text.RegularExpressions;

namespace Jarvis.Application.Integrations;

public static partial class McpToolSelection
{
    public const int MaxTools = 80;
    public const string All = "*";

    public static bool AllowsAll(IReadOnlyList<string>? tools) => tools is [All];

    public static string[] Normalize(IReadOnlyList<string>? tools)
    {
        var list = (tools ?? []).Select(tool => tool?.Trim() ?? string.Empty)
            .Where(tool => tool.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (list.Length == 1 && list[0] == All) return [All];
        if (list.Contains(All, StringComparer.Ordinal))
            throw new ArgumentException("Use * alone to allow every tool, or list exact tool names.");
        if (list.Length is < 1 or > MaxTools)
            throw new ArgumentException($"Allow 1 to {MaxTools} exact tool names, or * for every tool the server exposes.");
        if (list.Any(tool => tool.Length > 128 || !ToolPattern().IsMatch(tool)))
            throw new ArgumentException("Tool names must be 1 to 128 letters, numbers, underscores, dots, or hyphens.");
        return list;
    }

    public static string[] Apply(IReadOnlyList<string> current, string? mode, IReadOnlyList<string>? requested)
    {
        switch (Mode(mode))
        {
            case "replace":
                return Normalize(requested);
            case "add" when AllowsAll(current):
                return [All];
            case "add" when requested?.Any(tool => string.Equals(tool?.Trim(), All, StringComparison.Ordinal)) == true:
                return Normalize(requested);
            case "add":
                return Normalize(current.Concat(requested ?? []).ToArray());
            case "remove" when AllowsAll(Normalize(requested)):
                throw new ArgumentException("Replace the tool list instead of removing every tool.");
            case "remove" when AllowsAll(current):
                throw new ArgumentException("This server allows every exposed tool. Replace the list with the exact tools to keep.");
            case "remove":
                var remove = Normalize(requested).ToHashSet(StringComparer.Ordinal);
                var kept = current.Where(tool => !remove.Contains(tool)).ToArray();
                if (kept.Length == 0)
                    throw new ArgumentException("Keep at least one tool, or remove the server.");
                return Normalize(kept);
            default:
                throw new ArgumentException("Tool mode must be replace, add, or remove.");
        }
    }

    /// <summary>
    /// Returns the owner filter to store. Null means the operator allowlist is unchanged.
    /// </summary>
    public static string[]? ApplyHostFilter(IReadOnlyList<string>? currentFilter, IReadOnlyList<string> operatorAllow,
        string? mode, IReadOnlyList<string>? requested)
    {
        var action = Mode(mode);
        if (action == "add" && currentFilter is null) return null;
        if (action == "remove" && currentFilter is null && AllowsAll(operatorAllow))
            throw new ArgumentException("This server currently exposes its full configured toolset. Replace the list with the exact tools to keep.");

        var baseline = currentFilter ?? (AllowsAll(operatorAllow) ? [All] : operatorAllow.ToArray());
        var next = Apply(baseline, action, requested);
        if (AllowsAll(next)) return null;
        if (!AllowsAll(operatorAllow))
        {
            var allowed = operatorAllow.ToHashSet(StringComparer.Ordinal);
            var outside = next.Where(tool => !allowed.Contains(tool)).ToArray();
            if (outside.Length > 0)
                throw new ArgumentException(
                    $"These tools are not enabled on this server by its administrator: {string.Join(", ", outside)}.");
        }
        return next;
    }

    public static string[] Restrict(IReadOnlyList<string> configured, IReadOnlyList<string>? filter)
    {
        if (filter is null || filter.Count == 0 || AllowsAll(filter)) return configured.ToArray();
        if (AllowsAll(configured)) return filter.ToArray();
        var allowed = configured.ToHashSet(StringComparer.Ordinal);
        return filter.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }

    public static HostMcpOverride NextHostOverride(HostMcpOverride? current, IReadOnlyList<string> operatorAllow,
        bool? enabled, string? toolMode, IReadOnlyList<string>? requestedTools)
    {
        if (enabled is null && string.IsNullOrWhiteSpace(toolMode))
            throw new ArgumentException("Set enabled, a tool mode, or both.");
        var filter = current?.AllowedTools;
        if (!string.IsNullOrWhiteSpace(toolMode))
            filter = ApplyHostFilter(filter, operatorAllow, toolMode, requestedTools);
        return new HostMcpOverride(enabled ?? current?.Enabled ?? true, filter);
    }

    private static string Mode(string? mode)
    {
        var action = mode?.Trim().ToLowerInvariant();
        return action is "replace" or "add" or "remove"
            ? action
            : throw new ArgumentException("Tool mode must be replace, add, or remove.");
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolPattern();
}

public static class McpResourceUris
{
    public static void Validate(string? uri)
    {
        var value = uri?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 2_000 || value.Any(char.IsControl))
            throw new ArgumentException("Use a resource URI of 1 to 2000 characters without control characters.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return;
        if (parsed.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File resources are not available through Jarvis.");
        if (parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("MCP resource URIs must not use insecure HTTP.");
        if (!parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return;

        var host = parsed.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("MCP resource URIs must not target local hosts.");
        if (System.Net.IPAddress.TryParse(host, out var address) && !McpServerEndpointValidator.IsPublic(address))
            throw new ArgumentException("MCP resource URIs must not target private addresses.");
    }
}
