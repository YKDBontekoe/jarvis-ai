using System.ComponentModel;
using System.Text.Json;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;

namespace Jarvis.Agents;

/// <summary>
/// Lets Jarvis write its own MCP server when no catalog app fits: one Python file that runs in the isolated MCP
/// runner. Writing stores code and runs nothing; testing runs the draft and activation makes it live, and both
/// ask the owner first.
/// </summary>
internal sealed class CustomMcpAgentTools(ICustomMcpServerStore store, IUserMcpServerRegistry registry,
    ICurrentUser currentUser, McpToolHost mcpToolHost)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Description("Get the template and rules for writing a self-built MCP server (one Python file using the official MCP SDK), including how to keep a login between tool calls. Call this before WriteCustomMcpServer when no app in SearchMcpCatalog fits what the user wants, and before changing an app that loses its login or other session between calls. Read-only.")]
    public string GetCustomMcpTemplate() =>
        CustomMcpTemplate.Guidance + "\n\nTemplate:\n" + CustomMcpTemplate.Source;

    [Description("Save the code of a self-built MCP server for this user, creating it or saving a new draft revision when an app with this name exists. It only stores the code; nothing runs until TestCustomMcpServer or ActivateCustomMcpServer. If the code breaks the rules, the errors come back so you can fix them and call this again. Follow GetCustomMcpTemplate exactly. Each run starts with an empty home, so a server that logs in must keep its session with load_state and save_state from the template.")]
    public async Task<string> WriteCustomMcpServerAsync(
        [Description("Short app name, 1 to 60 letters, numbers, spaces, underscores or hyphens, for example Weather.")] string name,
        [Description("One sentence on what the app does, shown to the user.")] string description,
        [Description("The complete Python source, starting with the '# /// script' block.")] string source,
        [Description("Optional JSON array of keys the code reads from environment variables: [{\"variable\":\"WEATHER_API_KEY\",\"label\":\"Weather API key\",\"description\":\"From your account page\",\"required\":true}]. Use [] or omit when it needs none.")] string? secretsJson = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomMcpSecretRequest>? secrets;
        try
        {
            secrets = string.IsNullOrWhiteSpace(secretsJson)
                ? []
                : JsonSerializer.Deserialize<CustomMcpSecretRequest[]>(secretsJson, JsonOptions);
        }
        catch (JsonException) { return "secretsJson must be a JSON array of {variable, label, description, required}."; }

        try
        {
            var saved = await CustomMcpServerManager.SaveAsync(currentUser.OwnerId,
                new CustomMcpSaveRequest(name, description, source, secrets), store, registry, cancellationToken);
            var keys = saved.Secrets.Count == 0
                ? "It needs no keys."
                : "Keys it reads: " + string.Join(", ", saved.Secrets.Select(secret => secret.Key)) +
                  $". Ask for them with AskForMcpCredential (serverId '{saved.RegistryServerId}').";
            return $"Saved '{saved.Name}' revision {saved.Revision} (app id {saved.Id}, code sha256 {saved.DraftSha256}). " +
                   keys + " Next, call TestCustomMcpServer to check it starts and lists its tools." +
                   (CustomMcpTemplate.StateAdvice(source) is { } advice ? "\nWarning: " + advice : "");
        }
        catch (CustomMcpScriptException exception)
        {
            return "Not saved. Fix these and call WriteCustomMcpServer again:\n- " + string.Join("\n- ", exception.Errors);
        }
        catch (ArgumentException exception) { return $"Not saved: {exception.Message}"; }
    }

    [Description("Run the current draft of a self-built MCP server in the isolated MCP runner: start it, list its tools, and optionally call one tool with arguments. Returns the tools, the tool's output, or the error with the last lines the server printed (keys removed), so you can fix the code with WriteCustomMcpServer. Requires approval because it runs code.")]
    public async Task<string> TestCustomMcpServerAsync(
        [Description("The app id returned by WriteCustomMcpServer or ListCustomMcpServers.")] string id,
        [Description("Optional exact tool name to call once.")] string? toolName = null,
        [Description("JSON object of arguments for toolName. Use {} when there are none.")] string? argumentsJson = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var customId)) return "Pass the app id from WriteCustomMcpServer.";
        try
        {
            var result = await mcpToolHost.TestCustomServerAsync(customId, toolName, argumentsJson, cancellationToken);
            var draft = await store.GetAsync(currentUser.OwnerId, customId, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                ok = result.Ok,
                sha256 = result.Sha256,
                tools = result.Tools,
                error = result.Error,
                toolResult = result.ToolResult,
                diagnostics = result.Diagnostics,
                warning = draft is null ? null : CustomMcpTemplate.StateAdvice(draft.DraftSource),
                next = result.Ok
                    ? "If the tools look right, call ActivateCustomMcpServer with this sha256 to make it live."
                    : "Fix the code and call WriteCustomMcpServer again, then test again."
            }, JsonOptions);
        }
        catch (ArgumentException exception) { return exception.Message; }
    }

    [Description("Make the reviewed draft of a self-built MCP server live, so its tools are available from the next turn. Pass the sha256 of the code you tested; activation fails if the code changed since. Requires approval. Afterwards collect any missing keys with AskForMcpCredential.")]
    public async Task<string> ActivateCustomMcpServerAsync(
        [Description("The app id.")] string id,
        [Description("sha256 of the reviewed draft, from WriteCustomMcpServer or TestCustomMcpServer.")] string sha256,
        [Description("Only if the user explicitly says to switch it on without a passing test: their reason in their own words (10+ characters). Never invent one.")] string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var customId)) return "Pass the app id from WriteCustomMcpServer.";
        try
        {
            var (server, registered, _) = await CustomMcpServerManager.ActivateAsync(currentUser.OwnerId, customId,
                sha256, store, registry, cancellationToken, overrideReason);
            var missing = registered?.Secrets?.Where(secret => secret.Required && !secret.IsSet)
                .Select(secret => secret.Name).ToArray() ?? [];
            var next = missing.Length == 0
                ? "Its tools are available from the next message; use InvokeMcpTool with server '" +
                  server.RegistryServerId + "' for the rest of this turn."
                : $"It still needs these keys: {string.Join(", ", missing)}. Call AskForMcpCredential with serverId '{server.RegistryServerId}'.";
            var tested = server.LastTest is { Ok: true } test && test.Sha256 == server.ActiveSha256
                ? ""
                : " This revision has not passed a test yet.";
            return $"'{server.Name}' revision {server.ActiveRevision} is live (server id {server.RegistryServerId}).{tested} {next}";
        }
        catch (CustomMcpScriptException exception) { return "Not activated: " + string.Join(" ", exception.Errors); }
        catch (CustomMcpReviewException exception) when (exception.Code == "not_tested") { return "Not activated: this exact code has not passed a test. Call TestCustomMcpServer first. Only the user can decide to switch it on without a test."; }
        catch (ArgumentException exception) { return $"Not activated: {exception.Message}"; }
    }

    [Description("List the MCP servers Jarvis wrote for this user, with their ids, revisions, whether a draft is waiting for approval, the last test result, and a warning when an app seems to lose its login between tool calls. Read-only.")]
    public async Task<string> ListCustomMcpServersAsync(CancellationToken cancellationToken = default)
    {
        var servers = await store.ListAsync(currentUser.OwnerId, cancellationToken);
        if (servers.Count == 0) return "Jarvis has not written any apps for this user yet.";
        var advice = new Dictionary<Guid, string>();
        foreach (var server in servers)
        {
            var detail = await store.GetAsync(currentUser.OwnerId, server.Id, cancellationToken);
            var live = detail?.ActiveSource ?? detail?.DraftSource;
            if (live is not null && CustomMcpTemplate.StateAdvice(live) is { } text) advice[server.Id] = text;
        }
        return JsonSerializer.Serialize(servers.Select(server => new
        {
            server.Id,
            server.Name,
            server.Description,
            server.Revision,
            server.ActiveRevision,
            live = server.ActiveSha256 is not null,
            draftWaitingForApproval = server.HasUnapprovedChanges,
            draftSha256 = server.DraftSha256,
            serverId = server.RegistryServerId,
            keys = server.Secrets.Select(secret => secret.Key),
            lastTest = server.LastTest is null ? null : new { server.LastTest.Ok, server.LastTest.Tools, server.LastTest.Error },
            warning = advice.GetValueOrDefault(server.Id)
        }), JsonOptions);
    }

    [Description("Read the current draft source of one self-built MCP server, to change it. Read-only.")]
    public async Task<string> GetCustomMcpServerSourceAsync(
        [Description("The app id.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var customId)) return "Pass the app id from ListCustomMcpServers.";
        var detail = await store.GetAsync(currentUser.OwnerId, customId, cancellationToken);
        if (detail is null) return "That app does not exist.";
        return $"'{detail.Server.Name}' draft revision {detail.Server.Revision} (sha256 {detail.Server.DraftSha256})" +
               (detail.Server.HasUnapprovedChanges ? ", not yet approved" : ", live") + ":\n" + detail.DraftSource +
               (CustomMcpTemplate.StateAdvice(detail.DraftSource) is { } advice ? "\n\nWarning: " + advice : "");
    }
}
