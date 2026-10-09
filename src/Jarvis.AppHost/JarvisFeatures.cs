using Microsoft.Extensions.Configuration;

/// <summary>
/// Optional parts of a Jarvis deployment. They used to be extra Compose files layered on the base stack; now they
/// are switched on by name with <c>Jarvis:Features</c> (or <c>JARVIS_FEATURES</c>), comma- or space-separated:
/// <list type="bullet">
/// <item><c>browser</c> — isolated Playwright browser with a filtering egress proxy (BrowseTheWeb).</item>
/// <item><c>computer</c> — computer use: a sandbox Linux desktop with a visible browser, desktop tools, a shell and a
/// live view (UseComputer), behind the same egress proxy. It replaces <c>browser</c> when both are on.</item>
/// <item><c>github</c> — the GitHub MCP server bundled in the API image.</item>
/// <item><c>home-assistant</c> — Home Assistant's MCP endpoint (HOME_ASSISTANT_MCP_URL).</item>
/// <item><c>coding</c> — the approval-gated coding tool on a mounted checkout (CODING_REPO_PATH).</item>
/// <item><c>tunnel</c> — production behind an existing host proxy or Cloudflare Tunnel: Caddy only with the
/// direct-edge profile, and the API on 127.0.0.1:15082.</item>
/// <item><c>verification</c> — local e2e fixtures: fake MCP server and Codex app server (development only).</item>
/// </list>
/// </summary>
internal sealed record JarvisFeatures(bool Browser, bool GitHub, bool HomeAssistant, bool Coding, bool Tunnel,
    bool Verification, bool Computer = false)
{
    private static readonly string[] Known =
        ["browser", "computer", "github", "home-assistant", "coding", "tunnel", "verification"];

    /// <summary>The headless browser container; the computer sandbox brings its own browser instead.</summary>
    public bool HeadlessBrowser => Browser && !Computer;

    /// <summary>The internal agents network and its egress proxy, shared by the browser and the computer sandbox.</summary>
    public bool AgentsNetwork => Browser || Computer;

    public static JarvisFeatures From(IConfiguration configuration)
    {
        var raw = configuration["Jarvis:Features"] ?? configuration["JARVIS_FEATURES"] ?? "";
        var names = raw.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var unknown = names.Except(Known).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"Unknown Jarvis feature(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", Known)}.");
        return new JarvisFeatures(names.Contains("browser"), names.Contains("github"),
            names.Contains("home-assistant"), names.Contains("coding"), names.Contains("tunnel"),
            names.Contains("verification"), names.Contains("computer"));
    }
}

/// <summary>Container images shared by local development and the production deployment.</summary>
internal static class JarvisImages
{
    public const string Postgres = "pgvector/pgvector";
    public const string PostgresTag = "pg18";
    public const string Embeddings = "ghcr.io/huggingface/text-embeddings-inference";
    public const string EmbeddingsTag = "cpu-1.8";
    public const string EmbeddingsModel = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2";
    public const string Playwright = "mcr.microsoft.com/playwright/mcp";
    public const string Squid = "ubuntu/squid";
    public const string ComputerSandboxContext = "infra/computer";
    public const string DefaultSentryDsn =
        "https://8404fd3049a3ad459533f7630bc7be42@o4512185266339840.ingest.de.sentry.io/4512185388302416";
}

/// <summary>Static MCP servers the optional features add to the API (and worker).</summary>
internal static class JarvisMcpServers
{
    public static IEnumerable<KeyValuePair<string, string>> GitHub() =>
    [
        new("Mcp__Servers__github__Name", "github"),
        new("Mcp__Servers__github__Transport", "stdio"),
        new("Mcp__Servers__github__Command", "/usr/local/bin/github-mcp-server"),
        new("Mcp__Servers__github__Arguments__0", "stdio"),
        new("Mcp__Servers__github__EnvironmentVariables__GITHUB_TOOLSETS", "repos,issues,pull_requests"),
        new("Mcp__Servers__github__CredentialProvider", "github"),
        new("Mcp__Servers__github__CredentialEnvironmentVariables__GITHUB_PERSONAL_ACCESS_TOKEN", "token"),
        new("Mcp__Servers__github__AllowedTools__0", "*"),
    ];

    public static IEnumerable<KeyValuePair<string, string>> HomeAssistant(string endpoint) =>
    [
        new("Mcp__Servers__home-assistant__Name", "home-assistant"),
        new("Mcp__Servers__home-assistant__Transport", "streamableHttp"),
        new("Mcp__Servers__home-assistant__Endpoint", endpoint),
        new("Mcp__Servers__home-assistant__CredentialProvider", "home-assistant"),
        new("Mcp__Servers__home-assistant__CredentialHeaders__Authorization", "token"),
        new("Mcp__Servers__home-assistant__CredentialHeaderPrefixes__Authorization", "Bearer"),
        new("Mcp__Servers__home-assistant__AllowedTools__0", "*"),
    ];

    private static readonly string[] BrowserTools =
    [
        "browser_navigate", "browser_navigate_back", "browser_snapshot", "browser_find", "browser_click",
        "browser_type", "browser_fill_form", "browser_select_option", "browser_press_key", "browser_wait_for",
        "browser_take_screenshot", "browser_console_messages",
    ];

    private static readonly string[] BrowserReadOnlyTools =
        ["browser_snapshot", "browser_find", "browser_take_screenshot", "browser_console_messages"];

    public static IEnumerable<KeyValuePair<string, string>> Browser(string endpoint)
    {
        yield return new("Mcp__Servers__0__Name", "playwright");
        yield return new("Mcp__Servers__0__Transport", "streamableHttp");
        yield return new("Mcp__Servers__0__Endpoint", endpoint);
        for (var i = 0; i < BrowserTools.Length; i++)
            yield return new($"Mcp__Servers__0__AllowedTools__{i}", BrowserTools[i]);
        for (var i = 0; i < BrowserReadOnlyTools.Length; i++)
            yield return new($"Mcp__Servers__0__AutoApprovedTools__{i}", BrowserReadOnlyTools[i]);
    }

    private static readonly string[] ComputerBrowserTools =
    [
        "browser_navigate", "browser_navigate_back", "browser_snapshot", "browser_find", "browser_click",
        "browser_type", "browser_fill_form", "browser_select_option", "browser_press_key", "browser_wait_for",
        "browser_take_screenshot", "browser_console_messages", "browser_hover", "browser_drag", "browser_tabs",
        "browser_handle_dialog",
    ];

    private static readonly string[] ComputerDesktopTools =
    [
        "computer_screenshot", "computer_click", "computer_double_click", "computer_move", "computer_drag",
        "computer_scroll", "computer_type", "computer_key", "computer_wait", "computer_shell",
    ];

    /// <summary>Starting a session (UseComputer) is the approval; inside it these still ask every time.</summary>
    private static readonly string[] ComputerAlwaysAsk = ["browser_fill_form", "computer_shell"];

    /// <summary>The sandbox's Playwright server (visible Chromium) and its desktop server.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Computer(string sandboxHost, string token)
    {
        yield return new("Computer__ControlUrl", $"http://{sandboxHost}:8932");
        yield return new("Computer__ViewUrl", $"http://{sandboxHost}:6080");
        yield return new("Computer__Token", token);
        foreach (var setting in ComputerServer("computer-browser", $"http://{sandboxHost}:8931/mcp", ComputerBrowserTools))
            yield return setting;
        foreach (var setting in ComputerServer("computer", $"http://{sandboxHost}:8932/mcp", ComputerDesktopTools))
            yield return setting;
        yield return new("Mcp__Servers__computer__Headers__Authorization", $"Bearer {token}");
    }

    private static IEnumerable<KeyValuePair<string, string>> ComputerServer(string name, string endpoint,
        string[] tools)
    {
        yield return new($"Mcp__Servers__{name}__Name", name);
        yield return new($"Mcp__Servers__{name}__Transport", "streamableHttp");
        yield return new($"Mcp__Servers__{name}__Endpoint", endpoint);
        for (var i = 0; i < tools.Length; i++)
            yield return new($"Mcp__Servers__{name}__AllowedTools__{i}", tools[i]);
        var autoApproved = tools.Except(ComputerAlwaysAsk).ToArray();
        for (var i = 0; i < autoApproved.Length; i++)
            yield return new($"Mcp__Servers__{name}__AutoApprovedTools__{i}", autoApproved[i]);
    }

    public static IEnumerable<KeyValuePair<string, string>> Verification(string fakeMcpScript) =>
    [
        new("Mcp__Servers__0__Name", "verification"),
        new("Mcp__Servers__0__Transport", "stdio"),
        new("Mcp__Servers__0__Command", "node"),
        new("Mcp__Servers__0__Arguments__0", fakeMcpScript),
        new("Mcp__Servers__0__AllowedTools__0", "github_create_issue"),
        new("Mcp__Servers__0__AllowedTools__1", "search_docs"),
        new("Mcp__Servers__0__AutoApprovedTools__0", "search_docs"),
        new("Mcp__Servers__0__CredentialProvider", "verification"),
        new("Mcp__Servers__0__CredentialEnvironmentVariables__TEST_TOKEN", "token"),
    ];
}
