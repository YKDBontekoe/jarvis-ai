using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Approvals;

/// <summary>
/// A group of approval-gated actions the owner can allow once. Later calls in the same category
/// run without a new card.
/// </summary>
public sealed record ApprovalCategory(string Key, string Label, bool CanRemember);

public static partial class ApprovalCategories
{
    public static ApprovalCategory Resolve(string? toolName, string? argumentsJson)
    {
        var name = CanonicalName(toolName);
        if (name.Length == 0) return Hidden("This action");

        if (name.Equals("BrowseTheWeb", StringComparison.Ordinal) ||
            name.StartsWith("browser_", StringComparison.OrdinalIgnoreCase))
            return Remember("browser", "Using the browser");

        if (name.Equals("computer_shell", StringComparison.Ordinal))
            return Remember("computer.shell", "Running commands on the sandbox computer");
        if (name.Equals("UseComputer", StringComparison.Ordinal) ||
            name.StartsWith("computer_", StringComparison.Ordinal))
            return Remember("computer", "Using the sandbox computer");

        if (Known.TryGetValue(name, out var known))
            return Remember(known.Key, known.Label);

        if (name.Equals("InvokeMcpTool", StringComparison.Ordinal))
            return McpCall(argumentsJson, "toolName", "mcp.invoke", "Using an integration",
                (tool, server) => $"Using {tool} on {server}");

        if (name.Equals("ReadMcpResource", StringComparison.Ordinal))
        {
            var serverRaw = Argument(argumentsJson, "server");
            var server = Segment(serverRaw);
            return server.Length == 0
                ? Hidden("Reading an integration resource")
                : Remember($"mcp.read.{server}", $"Reading resources on {Plain(serverRaw!)}");
        }

        if (name.Equals("GetMcpPrompt", StringComparison.Ordinal))
            return McpCall(argumentsJson, "name", "mcp.prompt", "Fetching an integration prompt",
                (prompt, server) => $"Fetching {prompt} from {server}");

        if (name.StartsWith("automation_", StringComparison.Ordinal))
        {
            var kind = name["automation_".Length..];
            var label = kind switch
            {
                "channel_message" => "Sending messages for automations",
                "agent_run" => "Starting agent tasks for automations",
                _ => "Running automation " + kind.Replace('_', ' ')
            };
            return Remember("automations." + Segment(kind), label);
        }

        return Remember("tool." + Segment(name), "Using " + Humanize(name));
    }

    public static bool IsSafeKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length is < 3 or > 120) return false;
        if (!char.IsAsciiLetter(key[0])) return false;
        foreach (var ch in key)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-')) return false;
        }

        return true;
    }

    private static ApprovalCategory McpCall(string? argumentsJson, string secondArgument, string keyPrefix,
        string hiddenLabel, Func<string, string, string> label)
    {
        var serverRaw = Argument(argumentsJson, "server");
        var secondRaw = Argument(argumentsJson, secondArgument);
        var server = Segment(serverRaw);
        var second = Segment(secondRaw);
        if (server.Length == 0 || second.Length == 0) return Hidden(hiddenLabel);
        return Remember($"{keyPrefix}.{server}.{second}", label(Plain(secondRaw!), Plain(serverRaw!)));
    }

    private static ApprovalCategory Remember(string key, string label)
    {
        label = label.Trim();
        if (label.Length > 80) label = label[..79].TrimEnd() + "…";
        if (label.Length == 0) label = "This action";
        return IsSafeKey(key)
            ? new ApprovalCategory(key, label, true)
            : Hidden(label);
    }

    private static ApprovalCategory Hidden(string label) => new("unknown", label, false);

    internal static string CanonicalName(string? toolName)
    {
        var name = toolName?.Trim() ?? string.Empty;
        if (name.EndsWith("Async", StringComparison.Ordinal) && name.Length > "Async".Length)
            name = name[..^"Async".Length];
        return name;
    }

    private static string? Argument(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                    _ => null
                };
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string Segment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
        }

        var text = builder.ToString().Trim('_');
        if (text.Length > 48) text = text[..48].Trim('_');
        return text;
    }

    private static string Plain(string value)
    {
        var trimmed = Whitespace().Replace(value.Trim(), " ");
        return trimmed.Length <= 40 ? trimmed : trimmed[..40].TrimEnd();
    }

    private static string Humanize(string name)
    {
        var spaced = CamelBoundary().Replace(name, "$1 $2").Replace('_', ' ').Replace('-', ' ');
        spaced = Whitespace().Replace(spaced, " ").Trim().ToLowerInvariant();
        return spaced.Length == 0 ? "this action" : spaced;
    }

    private static readonly Dictionary<string, (string Key, string Label)> Known = new(StringComparer.Ordinal)
    {
        ["ForgetMemory"] = ("memory.forget", "Forgetting memories"),
        ["ProposeGraphFact"] = ("graph.change", "Changing the knowledge graph"),
        ["CorrectGraphFact"] = ("graph.change", "Changing the knowledge graph"),
        ["MergeGraphEntities"] = ("graph.change", "Changing the knowledge graph"),
        ["ForgetGraphEntity"] = ("graph.change", "Changing the knowledge graph"),
        ["RunCodingTask"] = ("coding.run", "Running coding tasks"),
        ["ProposeJarvisFix"] = ("coding.fix", "Proposing changes to Jarvis"),
        ["SendWhatsAppMessage"] = ("whatsapp.send", "Sending WhatsApp messages"),
        ["DeleteExpense"] = ("expenses.delete", "Deleting expenses"),
        ["RemovePerson"] = ("people.remove", "Removing people"),
        ["GetDeviceLocation"] = ("devices.location", "Reading device location"),
        ["ReadDeviceClipboard"] = ("devices.clipboard", "Reading the clipboard"),
        ["OpenUrlOnDevice"] = ("devices.open_url", "Opening links on your devices"),
        ["RunAutomation"] = ("automations.run", "Running automations"),
        ["DelegateToAgent"] = ("agents.delegate", "Asking another agent"),
        ["AddMcpServer"] = ("integrations.add", "Adding integrations"),
        ["AddMcpStdioServer"] = ("integrations.add", "Adding integrations"),
        ["UpdateMcpServer"] = ("integrations.update", "Updating integrations"),
        ["SetMcpServerEnabled"] = ("integrations.enable", "Pausing or enabling integrations"),
        ["SetMcpServerTools"] = ("integrations.tools", "Choosing integration tools"),
        ["RemoveMcpServer"] = ("integrations.remove", "Removing integrations"),
        ["DiscoverMcpServerTools"] = ("integrations.discover", "Discovering integration tools"),
        ["InstallIntegrationPack"] = ("integrations.install", "Installing guided integrations"),
    };

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
