namespace Jarvis.Application.Integrations;

/// <summary>First-step choices for adding or managing MCP servers from chat.</summary>
public static class McpSetupCatalog
{
    public const string Prompt =
        "Help me add or manage an MCP server or integration in this chat. Show the setup card.";

    public static readonly IReadOnlyList<McpSetupOption> Options =
    [
        new("github", "GitHub", "Issues, pull requests, and repositories"),
        new("home-assistant", "Home Assistant", "Lights, sensors, and home devices"),
        new("calendar", "Calendar", "Today’s events from an ICS feed"),
        new("mail", "Mail", "Search and draft email"),
        new("contacts", "Contacts", "People you know"),
        new("https", "Custom HTTPS server", "A public Streamable HTTP MCP endpoint"),
        new("stdio", "npm or PyPI package", "Install with npx or uvx"),
        new("manage", "Manage what I have", "Pause, resume, or remove a connection")
    ];
}

public sealed record McpSetupOption(string Id, string Title, string Subtitle);
