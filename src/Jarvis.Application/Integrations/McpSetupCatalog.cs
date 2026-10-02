namespace Jarvis.Application.Integrations;

/// <summary>First-step choices for adding or managing MCP servers from chat.</summary>
public static class McpSetupCatalog
{
    public const string Prompt =
        "Help me connect an app or manage the ones I have, in this chat. Show the setup card.";

    public static readonly IReadOnlyList<McpSetupOption> Options =
    [
        new("github", "GitHub", "Repositories, issues, and pull requests"),
        new("home-assistant", "Home Assistant", "Lights, sensors, and home devices"),
        new("calendar", "Calendar", "See your events, and add new ones"),
        new("mail", "Mail", "Search your email and draft replies"),
        new("contacts", "Contacts", "Look up people you know"),
        new("https", "Another app", "Search for it by name, or paste its address"),
        new("stdio", "Install a connector", "Advanced: from npm or PyPI"),
        new("manage", "Manage my apps", "Pause, resume, or remove one")
    ];
}

public sealed record McpSetupOption(string Id, string Title, string Subtitle);
