namespace Jarvis.Application.Approvals;

/// <summary>
/// How much damage a tool call can do, which decides whether Jarvis may approve it on its own.
/// </summary>
public enum ToolRisk
{
    /// <summary>Reads or lists things and changes nothing.</summary>
    ReadOnly,

    /// <summary>Changes the owner's own Jarvis data in a way that can be undone or that keeps its history.</summary>
    ReversibleLocal,

    /// <summary>Reads something private (location, clipboard) that the owner should decide to share each time.</summary>
    SensitiveRead,

    /// <summary>Reaches outside Jarvis: sends, acts on a site or device, runs code, or changes integrations.</summary>
    Outbound,

    /// <summary>Deletes or merges data.</summary>
    Destructive,

    /// <summary>Not classified. Always asks.</summary>
    Unknown
}

/// <summary>
/// The risk class of every approval-gated built-in tool. Anything not listed is <see cref="ToolRisk.Unknown"/> and
/// keeps asking, so a new gated tool is safe until someone decides what it is.
/// </summary>
public static class ToolRiskPolicy
{
    private static readonly Dictionary<string, ToolRisk> Classes = new(StringComparer.Ordinal)
    {
        // Looks at an integration without changing anything.
        ["DiscoverMcpServerTools"] = ToolRisk.ReadOnly,
        ["ReadMcpResource"] = ToolRisk.ReadOnly,
        ["GetMcpPrompt"] = ToolRisk.ReadOnly,

        // The owner's own data; the knowledge graph keeps its history and the library item can be removed.
        // RunAutomation only starts a rule the owner already enabled, and the rule's own sensitive actions
        // still queue for approval.
        ["RunAutomation"] = ToolRisk.ReversibleLocal,
        ["ProposeGraphFact"] = ToolRisk.ReversibleLocal,
        ["CorrectGraphFact"] = ToolRisk.ReversibleLocal,
        ["ClipUrlToLibrary"] = ToolRisk.ReversibleLocal,

        ["GetDeviceLocation"] = ToolRisk.SensitiveRead,
        ["ReadDeviceClipboard"] = ToolRisk.SensitiveRead,

        ["SendWhatsAppMessage"] = ToolRisk.Outbound,
        ["DelegateToAgent"] = ToolRisk.Outbound,
        ["OpenUrlOnDevice"] = ToolRisk.Outbound,
        ["InvokeMcpTool"] = ToolRisk.Outbound,
        ["BrowseTheWeb"] = ToolRisk.Outbound,
        ["RunCodingTask"] = ToolRisk.Outbound,
        ["ProposeJarvisFix"] = ToolRisk.Outbound,
        ["InstallIntegrationPack"] = ToolRisk.Outbound,
        ["InstallMcpFromCatalog"] = ToolRisk.Outbound,
        ["AddMcpServer"] = ToolRisk.Outbound,
        ["AddMcpStdioServer"] = ToolRisk.Outbound,
        ["UpdateMcpServer"] = ToolRisk.Outbound,
        ["SetMcpServerEnabled"] = ToolRisk.Outbound,
        ["SetMcpServerTools"] = ToolRisk.Outbound,
        ["RemoveMcpServer"] = ToolRisk.Outbound,

        ["ForgetMemory"] = ToolRisk.Destructive,
        ["DeleteExpense"] = ToolRisk.Destructive,
        ["RemovePerson"] = ToolRisk.Destructive,
        ["ForgetGraphEntity"] = ToolRisk.Destructive,
        ["MergeGraphEntities"] = ToolRisk.Destructive,

        // Starting a crew of background tasks is a bigger decision than any single tool.
        ["RunMission"] = ToolRisk.Unknown
    };

    public static ToolRisk Classify(string? toolName)
    {
        var name = ApprovalCategories.CanonicalName(toolName);
        if (name.Length == 0) return ToolRisk.Unknown;
        if (name.StartsWith("browser_", StringComparison.OrdinalIgnoreCase)) return ToolRisk.Outbound;
        if (name.StartsWith("automation_", StringComparison.Ordinal)) return ToolRisk.Outbound;
        return Classes.TryGetValue(name, out var risk) ? risk : ToolRisk.Unknown;
    }

    /// <summary>True for a risk class that can ever be approved without asking.</summary>
    public static bool CanAutoApprove(ToolRisk risk) => risk is ToolRisk.ReadOnly or ToolRisk.ReversibleLocal;
}
