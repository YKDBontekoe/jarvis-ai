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
            "See today’s events from your calendar link, and optionally let Jarvis add new events.",
            "ics", ["calendar_events_list", "calendar_event_create"], "npx",
            ["-y", "@cocal/google-calendar-mcp"], null, true),
        new(IntegrationPackIds.Mail, "Mail", "mail",
            "Let Jarvis search your email and draft replies. You sign in once; never paste passwords into chat.",
            "oauth", ["list_emails", "search_emails", "draft_email"], "npx",
            ["-y", "@gongrzhe/server-gmail-autoauth-mcp"], null, false),
        new(IntegrationPackIds.Contacts, "Contacts", "contacts",
            "Let Jarvis look up people you know. You sign in once.",
            "oauth", ["list_contacts", "search_contacts"], "npx",
            ["-y", "@modelcontextprotocol/server-google-contacts"], null, false)
    ];

    public static IntegrationPack? Find(string? id) =>
        All.FirstOrDefault(pack => pack.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static async Task<IReadOnlyList<IntegrationPackStatus>> ListAsync(IIntegrationCredentialStore credentials,
        IUserMcpServerRegistry servers, Guid ownerId, CancellationToken cancellationToken)
    {
        var list = await servers.ListAsync(ownerId, cancellationToken);
        var statuses = new List<IntegrationPackStatus>();
        foreach (var pack in All)
        {
            var secrets = await credentials.GetStatusAsync(ownerId, IntegrationPackIds.Provider(pack.Id),
                cancellationToken);
            var mcp = list.FirstOrDefault(server =>
                server.Name.Contains(pack.Name, StringComparison.OrdinalIgnoreCase));
            statuses.Add(new IntegrationPackStatus(pack, secrets is not null || mcp is not null,
                secrets?.SecretNames.Contains("token", StringComparer.OrdinalIgnoreCase) == true,
                secrets?.SecretNames.Contains("ics_url", StringComparer.OrdinalIgnoreCase) == true,
                mcp?.Id));
        }
        return statuses;
    }
}

public static class IntegrationPackInstaller
{
    public static async Task<IntegrationPackStatus> InstallAsync(Guid ownerId, string packId,
        InstallIntegrationPackRequest request, IIntegrationCredentialStore credentials,
        IUserMcpServerRegistry servers, CancellationToken cancellationToken)
    {
        var pack = IntegrationPackCatalog.Find(packId)
                   ?? throw new ArgumentException("Unknown integration pack.");
        string? mcpId = null;
        if (pack.SupportsIcs && !string.IsNullOrWhiteSpace(request.IcsUrl))
        {
            var url = await McpServerEndpointValidator.ValidateAsync(request.IcsUrl, cancellationToken);
            await credentials.SaveSecretAsync(ownerId, IntegrationPackIds.Provider(pack.Id), "ics_url", url,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(request.IcsToken))
                await credentials.SaveSecretAsync(ownerId, IntegrationPackIds.Provider(pack.Id), "token",
                    request.IcsToken, cancellationToken);
        }
        var wantsMcp = !string.IsNullOrWhiteSpace(request.Endpoint) ||
                       !string.IsNullOrWhiteSpace(request.Command) || !pack.SupportsIcs;
        var tools = request.AllowedTools is { Count: > 0 } ? request.AllowedTools : pack.SuggestedTools;
        if (wantsMcp && !string.IsNullOrWhiteSpace(request.Endpoint))
        {
            var server = await servers.AddAsync(ownerId,
                new AddUserMcpServerRequest(pack.Name, request.Endpoint, tools), cancellationToken);
            mcpId = server.Id;
        }
        else if (wantsMcp)
        {
            var command = request.Command ?? pack.SuggestedCommand
                          ?? throw new ArgumentException("This pack needs an MCP command or HTTPS endpoint.");
            var arguments = request.Arguments ?? pack.SuggestedArguments ?? [];
            var server = await servers.AddStdioAsync(ownerId,
                new AddUserMcpStdioServerRequest(pack.Name, command, arguments, tools), cancellationToken);
            mcpId = server.Id;
        }
        var secrets = await credentials.GetStatusAsync(ownerId, IntegrationPackIds.Provider(pack.Id),
            cancellationToken);
        return new IntegrationPackStatus(pack, true,
            secrets?.SecretNames.Contains("token") == true,
            secrets?.SecretNames.Contains("ics_url") == true, mcpId);
    }
}
