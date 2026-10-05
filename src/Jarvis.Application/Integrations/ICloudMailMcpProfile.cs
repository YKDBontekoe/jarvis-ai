namespace Jarvis.Application.Integrations;

/// <summary>Reviewed, mail-only configuration for the pinned IMAP connector.</summary>
public static class ICloudMailMcpProfile
{
    public const string Package = "imap-mcp-server@2.1.0";
    public const string Name = "iCloud Mail";
    public const string SetupEnvironment = "JARVIS_ICLOUD_MAIL_SETUP";
    public const string SetupValue = "jarvis-icloud-mail-v1";
    public const string UsernameEnvironment = "IMAP_MCP_ACCOUNT_ICLOUD_IMAP_USERNAME";
    public const string PasswordEnvironment = "IMAP_MCP_ACCOUNT_ICLOUD_IMAP_PASSWORD";

    public static IReadOnlyList<string> ReadTools { get; } = Array.AsReadOnly(new[]
    {
        "imap_list_accounts", "imap_test_account", "imap_list_folders", "imap_folder_status",
        "imap_get_unread_count", "imap_search_emails", "imap_get_latest_emails", "imap_get_email"
    });

    public static IReadOnlyList<McpSecretBinding> Secrets { get; } = Array.AsReadOnly(new[]
    {
        new McpSecretBinding("icloud_email", McpSecretBindings.EnvironmentTarget, UsernameEnvironment, null,
            "iCloud email address", "Your full iCloud Mail address.", true),
        new McpSecretBinding("icloud_app_password", McpSecretBindings.EnvironmentTarget, PasswordEnvironment, null,
            "Apple app-specific password", "Create an app-specific password at account.apple.com; never use your Apple Account password.", true)
    });

    public static bool IsPackage(string? command, IReadOnlyList<string>? arguments) =>
        command == "npx" && arguments is ["-y", Package];

    public static bool Matches(string? name, string? command, IReadOnlyList<string>? arguments) =>
        string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) && IsPackage(command, arguments);

    public static string[] RestrictTools(IReadOnlyList<string> tools) =>
        McpToolSelection.AllowsAll(tools) ? ReadTools.ToArray()
        : tools.Where(tool => ReadTools.Contains(tool, StringComparer.Ordinal)).ToArray();
}
