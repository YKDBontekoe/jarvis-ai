using System.Text.RegularExpressions;

namespace Jarvis.Application.Integrations;

public static partial class McpStdioCommandValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "npx",
        "uvx",
        "/usr/local/bin/github-mcp-server",
        "github-mcp-server"
    };

    public static (string Command, string[] Arguments) Normalize(string? command, IReadOnlyList<string>? arguments)
    {
        var normalizedCommand = command?.Trim() ?? string.Empty;
        if (normalizedCommand.Length is < 1 or > 256)
            throw new ArgumentException("Use a command of 1 to 256 characters.");
        if (normalizedCommand.Contains('/') && !AllowedCommands.Contains(normalizedCommand))
            throw new ArgumentException("Only preinstalled host binaries or npx/uvx may start an agent-managed stdio MCP server.");
        if (!AllowedCommands.Contains(normalizedCommand))
            throw new ArgumentException("Command must be npx, uvx, or an approved host MCP binary.");

        var args = (arguments ?? []).Select(arg => arg?.Trim() ?? string.Empty).ToArray();
        if (args.Length is < 1 or > 24)
            throw new ArgumentException("Provide 1 to 24 command arguments.");
        if (args.Any(arg => arg.Length is < 1 or > 256 || !ArgumentPattern().IsMatch(arg)))
            throw new ArgumentException("Arguments must be 1 to 256 characters without shell metacharacters.");

        if (normalizedCommand is "npx" or "uvx")
        {
            var packageIndex = Array.FindIndex(args, arg => PackagePattern().IsMatch(arg));
            if (packageIndex < 0)
                throw new ArgumentException("Include an npm or PyPI package name in the arguments, for example -y @scope/server.");
            if (normalizedCommand == "npx" && !args.Contains("-y", StringComparer.Ordinal))
                throw new ArgumentException("npx must include -y so Jarvis can download the package non-interactively.");
        }

        return (normalizedCommand, args);
    }

    [GeneratedRegex(@"^[A-Za-z0-9@/._+-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentPattern();

    [GeneratedRegex(@"^(@[A-Za-z0-9._-]+/)?[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();
}
