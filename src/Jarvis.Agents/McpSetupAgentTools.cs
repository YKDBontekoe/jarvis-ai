using System.ComponentModel;
using System.Text.Json;
using Jarvis.Agents.Surfaces;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Realtime;
using Jarvis.Application.Surfaces;
using Jarvis.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Agents;

internal sealed class McpSetupAgentTools(
    IUiSurfaceRepository surfaces,
    IRealtimePublisher realtime,
    ICurrentUser currentUser,
    IIntegrationCredentialStore credentials,
    IUserMcpServerRegistry servers,
    Guid? conversationId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Description("Show the in-chat setup card for adding or managing MCP servers and integrations. Call this when the user wants to connect GitHub, Home Assistant, calendar, mail, contacts, a custom HTTPS MCP server, an npm/PyPI package, or to pause/remove something already connected. Do not send them to Settings. Do not also call RenderUi on this turn.")]
    public Task<string> OfferMcpSetupAsync(CancellationToken cancellationToken = default)
    {
        if (conversationId is not { } id)
            return Task.FromResult(
                "Offer MCP setup in the Jarvis app. Options: " +
                string.Join("; ", McpSetupCatalog.Options.Select(option => $"{option.Id} ({option.Title})")) +
                ". Ask which one they want, then continue in this conversation.");
        var items = JsonSerializer.Serialize(McpSetupCatalog.Options.Select(option => new
        {
            id = option.Id,
            title = option.Title,
            subtitle = option.Subtitle
        }), JsonOptions);
        var actions = JsonSerializer.Serialize(McpSetupCatalog.Options.Select(option => new
        {
            id = option.Id,
            label = option.Title,
            style = option.Id is "https" or "stdio" or "manage" ? "secondary" : "primary"
        }), JsonOptions);
        return UiSurfaceRender.RenderAsync(surfaces, realtime, currentUser, id, UiSurfaceKinds.Choice,
            "Connect an app",
            "Pick an app, and Jarvis sets it up with you right here.",
            items, null, actions, null, cancellationToken);
    }

    [Description("Show a secret field in this chat so the user can paste an integration token. Jarvis stores it encrypted and never puts the value in the transcript. Use after RequestMcpAuthorization returns needs_token, or when a host server such as GitHub needs a personal access token. provider is github, home-assistant, a jarvis-mcp- id, or a jarvis-pack- id.")]
    public Task<string> AskForMcpCredentialAsync(
        [Description("Credential provider slug, for example github or jarvis-mcp- followed by the server id.")]
        string provider,
        [Description("Optional card title.")] string? title = null,
        [Description("Optional supporting text.")] string? body = null,
        [Description("Secret name. Defaults to token.")] string? secretName = null,
        CancellationToken cancellationToken = default)
    {
        var slug = provider?.Trim() ?? string.Empty;
        if (!IntegrationCredentialProviders.AllowsChatSecret(slug))
            return Task.FromResult("That provider cannot collect a token from chat.");
        var name = string.IsNullOrWhiteSpace(secretName)
            ? IntegrationCredentialProviders.UserMcpTokenSecret
            : secretName.Trim();
        if (conversationId is not { } id)
            return Task.FromResult(
                $"Ask the user in the Jarvis app to save a token for '{slug}' as '{name}'. Do not collect it in spoken text.");
        var fields = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "token",
                label = "Access key",
                type = "secret",
                placeholder = "Paste the key",
                secretName = name
            }
        }, JsonOptions);
        var actions = JsonSerializer.Serialize(new[]
        {
            new { id = "save", label = "Save key", style = "primary" }
        }, JsonOptions);
        return UiSurfaceRender.RenderAsync(surfaces, realtime, currentUser, id, UiSurfaceKinds.Form,
            string.IsNullOrWhiteSpace(title) ? "Save an access key" : title.Trim(),
            string.IsNullOrWhiteSpace(body)
                ? "Stored encrypted in Jarvis and never shown in chat again."
                : body.Trim(),
            null, fields, actions, slug, cancellationToken);
    }

    [Description("Install a guided integration pack from chat: calendar, mail, or contacts. For calendar, pass icsUrl; an ICS token should already be stored with AskForMcpCredential on provider jarvis-pack-calendar. For mail or contacts, omit endpoint to install the suggested stdio package, then call RequestMcpAuthorization. Requires approval.")]
    public async Task<string> InstallIntegrationPackAsync(
        [Description("calendar, mail, or contacts.")] string packId,
        [Description("Public HTTPS ICS/iCal URL. Calendar pack.")] string? icsUrl = null,
        [Description("Optional public HTTPS MCP endpoint instead of the suggested stdio package.")] string? endpoint = null,
        [Description("Optional stdio command such as npx. Omit to use the pack default.")] string? command = null,
        [Description("Optional JSON array of command arguments.")] string? argumentsJson = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<string>? arguments = null;
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return "argumentsJson must be a JSON array of strings.";
                arguments = document.RootElement.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray();
            }
            var status = await IntegrationPackInstaller.InstallAsync(currentUser.OwnerId, packId,
                new InstallIntegrationPackRequest(icsUrl, null, endpoint, command, arguments, null),
                credentials, servers, cancellationToken);
            var mcp = status.McpServerId is null ? "No MCP server was attached." :
                $"MCP server id {status.McpServerId}. If it needs authorization, call RequestMcpAuthorization with that id.";
            return $"Installed the {status.Pack.Name} pack. ICS feed: {(status.HasIcs ? "saved" : "none")}. {mcp}";
        }
        catch (ArgumentException exception) { return $"Could not install that pack: {exception.Message}"; }
        catch (JsonException) { return "argumentsJson must be a JSON array of strings."; }
    }
}

internal sealed class McpSetupToolContributor(
    IUiSurfaceRepository surfaces,
    IRealtimePublisher realtime,
    ICurrentUser currentUser,
    IIntegrationCredentialStore credentials,
    IUserMcpServerRegistry servers) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new McpSetupAgentTools(surfaces, realtime, currentUser, credentials, servers,
            context.ConversationId);
        yield return AIFunctionFactory.Create(tools.OfferMcpSetupAsync);
        yield return AIFunctionFactory.Create(tools.AskForMcpCredentialAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.InstallIntegrationPackAsync));
    }
}

internal sealed class McpContextContributor(
    IUserMcpServerRegistry servers,
    IOwnerMcpPolicyStore policy,
    IIntegrationCredentialStore credentials,
    IConfiguration configuration,
    ICurrentUser currentUser) : IAgentContextContributor
{
    public int Order => 35;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new McpContextProvider(servers, policy, credentials, configuration, currentUser.OwnerId)];
}

internal sealed class McpContextProvider(
    IUserMcpServerRegistry servers,
    IOwnerMcpPolicyStore policy,
    IIntegrationCredentialStore credentials,
    IConfiguration configuration,
    Guid ownerId) : MessageAIContextProvider
{
    internal const string Prefix = "MCP and integrations";

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(Prefix).AppendLine(" (manage these in this chat with OfferMcpSetup; never send the user to Settings to start):");
        var owned = await servers.ListAsync(ownerId, cancellationToken);
        if (owned.Count == 0) builder.AppendLine("- no user-registered MCP servers");
        foreach (var server in owned.Take(12))
        {
            builder.Append("- ").Append(server.Name).Append(" (").Append(server.Id).Append(") ");
            builder.Append(server.Enabled ? "enabled" : "paused");
            builder.Append(server.HasToken ? ", token saved" : ", needs authorization");
            builder.AppendLine();
        }
        var overrides = await policy.GetAsync(ownerId, cancellationToken);
        foreach (var host in McpServerConfiguration.Read(configuration))
        {
            if (string.IsNullOrWhiteSpace(host.Name)) continue;
            var provider = string.IsNullOrWhiteSpace(host.CredentialProvider)
                ? host.Name.ToLowerInvariant()
                : host.CredentialProvider;
            var secrets = await credentials.GetSecretsAsync(ownerId, provider, cancellationToken);
            var enabled = !overrides.TryGetValue(host.Name, out var hostPolicy) || hostPolicy.Enabled;
            builder.Append("- host ").Append(host.Name).Append(' ').Append(enabled ? "enabled" : "paused");
            builder.Append(secrets is null ? ", needs authorization via AskForMcpCredential provider " + provider
                : ", credentials stored");
            builder.AppendLine();
        }
        foreach (var pack in await IntegrationPackCatalog.ListAsync(credentials, servers, ownerId, cancellationToken))
        {
            builder.Append("- pack ").Append(pack.Pack.Name).Append(pack.Installed ? " installed" : " not installed");
            builder.AppendLine();
        }
        builder.Append("When the user wants to connect a tool, call OfferMcpSetup. Collect tokens with AskForMcpCredential. Start OAuth with RequestMcpAuthorization. Pause or remove with SetMcpServerEnabled / RemoveMcpServer.");
        return [new ChatMessage(ChatRole.User, builder.ToString())];
    }
}
