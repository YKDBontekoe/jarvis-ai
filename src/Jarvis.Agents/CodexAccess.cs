using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Agents;

/// <summary>
/// What the Codex app-server may do on this host while it produces a reply: its own shell, file writes inside the
/// per-turn scratch workspace, network from tools, and which optional Codex features are on. Jarvis functions are
/// unaffected: they always run in Jarvis, behind its approval cards. Configured under <c>Codex:Access</c>.
/// </summary>
public sealed record CodexAccess
{
    public const string ReadOnly = "read-only";
    public const string WorkspaceWrite = "workspace-write";
    public const string DangerFullAccess = "danger-full-access";

    /// <summary>Codex features that stay off even in the open profile: they drive a browser or desktop on their own,
    /// bypassing Jarvis's approval-gated browser, or spawn extra agents that Jarvis cannot account for.</summary>
    internal static readonly string[] AlwaysDisabledFeatures =
    [
        "computer_use", "browser_use", "browser_use_external", "in_app_browser", "multi_agent", "multi_agent_v2"
    ];

    /// <summary>One of <see cref="ReadOnly"/>, <see cref="WorkspaceWrite"/> or <see cref="DangerFullAccess"/>.</summary>
    public string Sandbox { get; init; } = WorkspaceWrite;

    /// <summary>Lets commands run by Codex reach the network (package installs, curl, git).</summary>
    public bool AllowNetwork { get; init; } = true;

    /// <summary>Offers Codex's own shell tool. Commands run inside <see cref="Sandbox"/> without an approval card.</summary>
    public bool AllowShell { get; init; } = true;

    /// <summary>Extra directories writable in <see cref="WorkspaceWrite"/> mode, besides the per-turn scratch directory.</summary>
    public IReadOnlyList<string> WritableRoots { get; init; } = [];

    /// <summary>Optional Codex features to leave off. Empty keeps <see cref="AlwaysDisabledFeatures"/> only.</summary>
    public IReadOnlyList<string> ExtraDisabledFeatures { get; init; } = [];

    /// <summary>The previous, locked-down behavior: read-only, no network, no shell, no optional features.</summary>
    public static CodexAccess Strict { get; } = new()
    {
        Sandbox = ReadOnly,
        AllowNetwork = false,
        AllowShell = false,
        ExtraDisabledFeatures = ["skill_search", "image_generation", "apps", "plugins"]
    };

    public static CodexAccess Open { get; } = new();

    public static CodexAccess From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Codex:Access");
        var access = new CodexAccess
        {
            Sandbox = section["Sandbox"] ?? Open.Sandbox,
            AllowNetwork = section.GetValue("AllowNetwork", Open.AllowNetwork),
            AllowShell = section.GetValue("AllowShell", Open.AllowShell),
            WritableRoots = section.GetSection("WritableRoots").Get<string[]>() ?? [],
            ExtraDisabledFeatures = section.GetSection("DisabledFeatures").Get<string[]>() ?? []
        };
        access.Validate();
        return access;
    }

    internal void Validate()
    {
        if (Sandbox is not (ReadOnly or WorkspaceWrite or DangerFullAccess))
            throw new InvalidOperationException(
                $"Codex:Access:Sandbox must be {ReadOnly}, {WorkspaceWrite} or {DangerFullAccess}.");
        if (WritableRoots.Any(root => !Path.IsPathRooted(root)))
            throw new InvalidOperationException("Codex:Access:WritableRoots must be absolute paths.");
    }

    /// <summary>Features passed to <c>--disable</c>.</summary>
    internal IEnumerable<string> DisabledFeatures(bool webSearch)
    {
        var features = new List<string>(AlwaysDisabledFeatures);
        if (!AllowShell) features.AddRange(["shell_tool", "shell_snapshot"]);
        // Hosted web search and the shell both run through the code-mode host; with neither it is not needed.
        if (!AllowShell && !webSearch) features.Add("code_mode_host");
        features.AddRange(ExtraDisabledFeatures);
        return features.Distinct(StringComparer.Ordinal);
    }

    /// <summary>The thread-level sandbox mode for <c>thread/start</c>.</summary>
    internal string ThreadSandbox => Sandbox;

    /// <summary>The per-turn sandbox policy for <c>turn/start</c>.</summary>
    internal JsonObject TurnSandboxPolicy(string scratch) => Sandbox switch
    {
        DangerFullAccess => new JsonObject { ["type"] = "dangerFullAccess" },
        WorkspaceWrite => new JsonObject
        {
            ["type"] = "workspaceWrite",
            ["writableRoots"] = new JsonArray(new[] { scratch }.Concat(WritableRoots)
                .Select(root => (JsonNode)JsonValue.Create(root)!).ToArray()),
            ["networkAccess"] = AllowNetwork
        },
        _ => new JsonObject { ["type"] = "readOnly", ["networkAccess"] = AllowNetwork }
    };

    /// <summary>Proxy and CA settings the child needs to reach the network. They describe the host's route out, not
    /// application secrets, so they are passed through only when network access is on.</summary>
    internal static readonly string[] NetworkEnvironment =
    [
        "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "NO_PROXY", "https_proxy", "http_proxy", "all_proxy", "no_proxy",
        "NODE_EXTRA_CA_CERTS", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "GIT_SSL_CAINFO", "CODEX_CA_CERTIFICATE"
    ];
}
