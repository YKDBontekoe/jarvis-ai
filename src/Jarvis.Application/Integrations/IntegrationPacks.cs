namespace Jarvis.Application.Integrations;

public static class IntegrationPackIds
{
    public const string Calendar = "calendar";
    public const string Mail = "mail";
    public const string Contacts = "contacts";

    public static string Provider(string packId) => $"jarvis-pack-{packId}";
}

public sealed record IntegrationPack(
    string Id,
    string Name,
    string Category,
    string Description,
    string AuthKind,
    IReadOnlyList<string> SuggestedTools,
    string? SuggestedCommand,
    IReadOnlyList<string>? SuggestedArguments,
    string? SuggestedEndpoint,
    bool SupportsIcs);

public sealed record IntegrationPackStatus(IntegrationPack Pack, bool Installed, bool HasToken, bool HasIcs,
    string? McpServerId);

public sealed record InstallIntegrationPackRequest(
    string? IcsUrl,
    string? IcsToken,
    string? Endpoint,
    string? Command,
    IReadOnlyList<string>? Arguments,
    IReadOnlyList<string>? AllowedTools);

public sealed record CalendarEventRecord(string Title, DateTimeOffset StartAt, DateTimeOffset? EndAt);

public interface ICalendarFeed
{
    Task<IReadOnlyList<CalendarEventRecord>> ListUpcomingAsync(Guid ownerId, DateTimeOffset from, DateTimeOffset until,
        CancellationToken cancellationToken);
}

public static class IntegrationPackCatalog
{
    public static readonly IReadOnlyList<IntegrationPack> All =
    [
        new(IntegrationPackIds.Calendar, "Calendar", "calendar",
            "Subscribe to an ICS/iCal feed for today’s events, and optionally attach a calendar MCP server so Jarvis can list or create events.",
            "ics", ["calendar_events_list", "calendar_event_create"], "npx",
            ["-y", "@cocal/google-calendar-mcp"], null, true),
        new(IntegrationPackIds.Mail, "Mail", "mail",
            "Connect a mail MCP server so Jarvis can search and draft messages. Store the token through OAuth or Integrations — never in chat.",
            "oauth", ["list_emails", "search_emails", "draft_email"], "npx",
            ["-y", "@gongrzhe/server-gmail-autoauth-mcp"], null, false),
        new(IntegrationPackIds.Contacts, "Contacts", "contacts",
            "Connect a contacts MCP server so Jarvis can look up people you know. Authorization stays in Integrations.",
            "oauth", ["list_contacts", "search_contacts"], "npx",
            ["-y", "@modelcontextprotocol/server-google-contacts"], null, false)
    ];

    public static IntegrationPack? Find(string? id) =>
        All.FirstOrDefault(pack => pack.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase));
}
