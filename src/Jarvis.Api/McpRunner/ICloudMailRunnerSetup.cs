using System.Text.Json;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;

namespace Jarvis.Api.McpRunner;

/// <summary>Creates non-secret account metadata inside one connector's disposable home.</summary>
internal static class ICloudMailRunnerSetup
{
    public static void Prepare(McpRunnerLaunch launch, string workDirectory)
    {
        if (!Applies(launch)) return;
        if (!launch.Environment.TryGetValue(ICloudMailMcpProfile.UsernameEnvironment, out var username) ||
            string.IsNullOrWhiteSpace(username) ||
            !launch.Environment.TryGetValue(ICloudMailMcpProfile.PasswordEnvironment, out var password) ||
            string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Save the iCloud Mail address and app-specific password in Jarvis first.");

        var directory = Path.Combine(workDirectory, ".imap-mcp");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Empty credential fields are the upstream marker for environment-managed credentials. The connector
        // creates its own encryption key and captures the injected values in memory; no password is written here.
        var accounts = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new
            {
                id = "icloud", name = "iCloud", host = "imap.mail.me.com", port = 993,
                tls = true, allowStartTLS = false, user = "", password = "", email = username
            }
        });
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(Path.Combine(directory, "accounts.json"), options);
        file.Write(accounts);
    }

    public static void RestrictProcess(McpRunnerLaunch launch, System.Diagnostics.ProcessStartInfo start)
    {
        if (!Applies(launch)) return;
        // Set after all caller/operator environment variables. No mutation or credential-management tools are
        // registered, even if a stale server definition requests * or attempts to override the read-only flag.
        start.Environment["IMAP_MCP_READ_ONLY"] = "true";
        start.Environment["IMAP_MCP_ENABLED_TOOLS"] = string.Join(',', ICloudMailMcpProfile.ReadTools);
        start.Environment.Remove(ICloudMailMcpProfile.SetupEnvironment);
    }

    private static bool Applies(McpRunnerLaunch launch) =>
        ICloudMailMcpProfile.IsPackage(launch.Command, launch.Arguments) &&
        launch.Environment.TryGetValue(ICloudMailMcpProfile.SetupEnvironment, out var enabled) &&
        enabled == ICloudMailMcpProfile.SetupValue;
}
