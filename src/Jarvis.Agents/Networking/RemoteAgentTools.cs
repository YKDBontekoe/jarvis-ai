using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Agents;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Networking;

internal sealed class RemoteAgentTools(
    IRemoteAgentRepository agents,
    IIntegrationCredentialStore credentials,
    IHttpClientFactory httpClients,
    ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Description("List remote Agent2Agent peers the user connected, with whether each is enabled.")]
    public async Task<string> ListRemoteAgentsAsync(CancellationToken cancellationToken = default)
    {
        var list = await agents.ListAsync(currentUser.OwnerId, cancellationToken);
        if (list.Count == 0) return "No remote agents are connected. The user can add them under Settings → Agents.";
        var builder = new StringBuilder();
        foreach (var agent in list)
            builder.Append("- ").Append(agent.Name).Append(" [").Append(agent.Enabled ? "on" : "paused")
                .Append(", id ").Append(agent.Id).Append("]: ").AppendLine(agent.Url);
        return builder.ToString();
    }

    [Description("Send a task to a connected remote agent over the Agent2Agent protocol and return its reply. Use this when the user asks you to involve another assistant they have registered. Do not forward secrets.")]
    public async Task<string> DelegateToAgentAsync(
        [Description("The remote agent id from ListRemoteAgents, or its exact name.")] string agent,
        [Description("The message to send. Keep it self-contained; the remote agent does not have this conversation.")] string message,
        CancellationToken cancellationToken = default)
    {
        var text = message?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 8_000) return "Send a message of 1 to 8,000 characters.";
        if (MemoryAgentTools.LooksLikeSecret(text)) return "That looks like a credential, so it was not sent.";

        var ownerId = currentUser.OwnerId;
        var list = await agents.ListAsync(ownerId, cancellationToken);
        RemoteAgentRecord? peer = Guid.TryParse(agent, out var id)
            ? list.FirstOrDefault(item => item.Id == id)
            : list.FirstOrDefault(item => string.Equals(item.Name, agent.Trim(), StringComparison.OrdinalIgnoreCase));
        if (peer is null) return "No connected agent matched that name. Call ListRemoteAgents first.";
        if (!peer.Enabled) return $"{peer.Name} is paused. The user can enable it under Settings → Agents.";

        var secrets = await credentials.GetSecretsAsync(ownerId,
            IntegrationCredentialProviders.RemoteAgentPrefix + peer.Id.ToString("N"), cancellationToken);
        var token = secrets?.GetValueOrDefault("token");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, peer.Url);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = Guid.CreateVersion7().ToString("N"),
                method = "message/send",
                @params = new
                {
                    message = new
                    {
                        role = "user",
                        parts = new[] { new { kind = "text", text } }
                    }
                }
            }, options: Json);
            using var response = await httpClients.CreateClient("a2a").SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await agents.RecordUseAsync(ownerId, peer.Id, $"HTTP {(int)response.StatusCode}", cancellationToken);
                return $"{peer.Name} rejected the request ({(int)response.StatusCode}).";
            }
            await agents.RecordUseAsync(ownerId, peer.Id, null, cancellationToken);
            return Describe(peer.Name, body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await agents.RecordUseAsync(ownerId, peer.Id, exception.Message, cancellationToken);
            return $"{peer.Name} is unreachable right now.";
        }
    }

    internal static string Describe(string name, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
                return $"{name} returned an error: {error.GetProperty("message").GetString()}";
            if (!root.TryGetProperty("result", out var result)) return Limit($"{name} replied: {body}");
            if (result.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                var texts = parts.EnumerateArray()
                    .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text));
                var combined = string.Join("\n", texts);
                if (!string.IsNullOrWhiteSpace(combined)) return Limit($"{name} replied:\n{combined}");
            }
            if (result.TryGetProperty("status", out var status) &&
                status.TryGetProperty("state", out var state))
                return $"{name} is {state.GetString()}.";
            return Limit($"{name} replied: {result}");
        }
        catch (JsonException)
        {
            return Limit($"{name} replied: {body}");
        }
    }

    private static string Limit(string value) => value.Length <= 4_000 ? value : value[..4_000];
}

internal sealed class RemoteAgentToolContributor(
    IRemoteAgentRepository agents,
    IIntegrationCredentialStore credentials,
    IHttpClientFactory httpClients,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new RemoteAgentTools(agents, credentials, httpClients, currentUser);
        yield return AIFunctionFactory.Create(tools.ListRemoteAgentsAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.DelegateToAgentAsync));
    }
}

internal sealed class RemoteAgentContextContributor(IRemoteAgentRepository agents) : IAgentContextContributor
{
    public int Order => 45;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new RemoteAgentContextProvider(agents, context.OwnerId)];
}

internal sealed class RemoteAgentContextProvider(IRemoteAgentRepository agents, Guid ownerId) : MessageAIContextProvider
{
    internal const string Prefix = "Remote agents";

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var list = (await agents.ListAsync(ownerId, cancellationToken)).Where(agent => agent.Enabled).ToArray();
        var builder = new StringBuilder();
        builder.Append(Prefix).AppendLine(" the user connected over Agent2Agent (call ListRemoteAgents, then DelegateToAgent after approval):");
        if (list.Length == 0) builder.AppendLine("- none yet");
        foreach (var agent in list)
            builder.Append("- ").Append(agent.Name).Append(" (").Append(agent.Id).AppendLine(")");
        return [new ChatMessage(ChatRole.User, builder.ToString())];
    }
}
