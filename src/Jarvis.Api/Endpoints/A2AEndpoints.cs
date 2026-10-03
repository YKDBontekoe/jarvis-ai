using System.Text.Json;
using Jarvis.Application.Agents;
using Jarvis.Application.Integrations;
using Jarvis.Api.Conversations;
using Jarvis.Api.Security;
using Jarvis.Application.Conversations;

namespace Jarvis.Api.Endpoints;

public sealed record RemoteAgentDto(Guid Id, string Name, string Url, bool Enabled, bool HasToken,
    DateTimeOffset? LastUsedAt, string? LastError, DateTimeOffset CreatedAt);
public sealed record SaveRemoteAgentBody(string? Name, string? Url, bool Enabled, string? Token);
public sealed record A2ATokenDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt,
    string? Token);
public sealed record CreateA2ATokenRequest(string? Name);

internal static class A2AEndpoints
{
    public static IEndpointRouteBuilder MapA2AProtocol(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/agent-card.json", (IConfiguration configuration, HttpRequest request) =>
            Results.Json(AgentCard(configuration, request))).WithName("GetA2AAgentCard").AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.PublicPolicy);

        app.MapPost("/a2a", async (HttpRequest request, JsonElement body, IA2ATokenRepository tokens,
            IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var token = request.Headers.Authorization.ToString();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token["Bearer ".Length..].Trim();
            else
                token = request.Headers["X-A2A-Token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
                return Results.Unauthorized();
            var ownerId = await tokens.FindOwnerAsync(token, ct);
            if (ownerId is null) return Results.Unauthorized();

            if (!body.TryGetProperty("method", out var methodElement) || methodElement.GetString() is not { } method)
                return JsonRpcError(body, -32600, "Missing method.");
            var id = body.TryGetProperty("id", out var rpcId) ? rpcId.Clone() : default;
            if (method is "message/send" or "tasks/send")
            {
                var text = ReadText(body);
                if (string.IsNullOrWhiteSpace(text) || text.Length > 32_000)
                    return JsonRpcError(body, -32602, "Send a text message of 1 to 32,000 characters.");
                await using var scope = scopes.CreateOwnerScope(ownerId.Value);
                var store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
                var conversation = await store.CreateAsync(ownerId.Value, TitleFor(text), ct);
                var ownedTurns = scope.ServiceProvider.GetRequiredService<ConversationTurnService>();
                var result = await ownedTurns.SendAsync(ownerId.Value, conversation.Id, text, ct);
                return JsonRpcResult(id, result, conversation.Id);
            }
            if (method is "agent/authenticatedExtendedCard" or "agent/getAuthenticatedExtendedCard")
                return JsonRpcResult(id, AgentCard(request.HttpContext.RequestServices.GetRequiredService<IConfiguration>(), request), default);
            return JsonRpcError(body, -32601, $"Unsupported method '{method}'.");
        }).WithName("A2AJsonRpc").AllowAnonymous().DisableAntiforgery()
            .RequireRateLimiting(ApiRateLimiting.PublicPolicy);

        return app;
    }

    public static RouteGroupBuilder MapA2AManagementEndpoints(this RouteGroupBuilder api)
    {
        var agents = api.MapGroup("/agents");
        agents.MapGet("", async (IRemoteAgentRepository repository, IIntegrationCredentialStore credentials,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var list = new List<RemoteAgentDto>();
            foreach (var agent in await repository.ListAsync(currentUser.OwnerId, ct))
            {
                var status = await credentials.GetStatusAsync(currentUser.OwnerId,
                    IntegrationCredentialProviders.RemoteAgentPrefix + agent.Id.ToString("N"), ct);
                list.Add(ToDto(agent, status?.SecretNames.Contains("token") == true));
            }
            return Results.Ok(list);
        }).WithName("ListRemoteAgents");

        agents.MapPost("", async (SaveRemoteAgentBody request, IRemoteAgentRepository repository,
            IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var (name, url) = RemoteAgentValidation.Normalize(request.Name, request.Url);
                var created = await repository.CreateAsync(currentUser.OwnerId, name, url, request.Enabled, ct);
                if (!string.IsNullOrWhiteSpace(request.Token))
                    await credentials.SaveSecretAsync(currentUser.OwnerId,
                        IntegrationCredentialProviders.RemoteAgentPrefix + created.Id.ToString("N"), "token",
                        request.Token.Trim(), ct);
                return Results.Created($"/api/v1/agents/{created.Id}", ToDto(created, request.Token is { Length: > 0 }));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("agent", exception.Message);
            }
        }).WithName("CreateRemoteAgent");

        agents.MapPut("/{id:guid}", async (Guid id, SaveRemoteAgentBody request, IRemoteAgentRepository repository,
            IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var (name, url) = RemoteAgentValidation.Normalize(request.Name, request.Url);
                var updated = await repository.UpdateAsync(currentUser.OwnerId, id, name, url, request.Enabled, ct);
                if (updated is null) return Results.NotFound();
                if (!string.IsNullOrWhiteSpace(request.Token))
                    await credentials.SaveSecretAsync(currentUser.OwnerId,
                        IntegrationCredentialProviders.RemoteAgentPrefix + id.ToString("N"), "token",
                        request.Token.Trim(), ct);
                var status = await credentials.GetStatusAsync(currentUser.OwnerId,
                    IntegrationCredentialProviders.RemoteAgentPrefix + id.ToString("N"), ct);
                return Results.Ok(ToDto(updated, status?.SecretNames.Contains("token") == true));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("agent", exception.Message);
            }
        }).WithName("UpdateRemoteAgent");

        agents.MapDelete("/{id:guid}", async (Guid id, IRemoteAgentRepository repository,
            IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await repository.DeleteAsync(currentUser.OwnerId, id, ct)) return Results.NotFound();
            await credentials.DeleteAsync(currentUser.OwnerId,
                IntegrationCredentialProviders.RemoteAgentPrefix + id.ToString("N"), ct);
            return Results.NoContent();
        }).WithName("DeleteRemoteAgent");

        var tokens = api.MapGroup("/a2a/tokens");
        tokens.MapGet("", async (IA2ATokenRepository repository, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok((await repository.ListAsync(currentUser.OwnerId, ct))
                .Select(token => new A2ATokenDto(token.Id, token.Name, token.CreatedAt, token.LastUsedAt, null))))
            .WithName("ListA2ATokens");

        tokens.MapPost("", async (CreateA2ATokenRequest request, IA2ATokenRepository repository,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var name = RemoteAgentValidation.NormalizeTokenName(request.Name);
                var (record, token) = await repository.CreateAsync(currentUser.OwnerId, name, ct);
                return Results.Created($"/api/v1/a2a/tokens/{record.Id}",
                    new A2ATokenDto(record.Id, record.Name, record.CreatedAt, record.LastUsedAt, token));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("name", exception.Message);
            }
        }).WithName("CreateA2AToken");

        tokens.MapDelete("/{id:guid}", async (Guid id, IA2ATokenRepository repository, ICurrentUser currentUser,
                CancellationToken ct) =>
            await repository.DeleteAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteA2AToken");

        return api;
    }

    private static object AgentCard(IConfiguration configuration, HttpRequest request)
    {
        var publicBase = configuration["Jarvis:PublicBaseUrl"]?.TrimEnd('/')
                         ?? configuration["Channels:PublicBaseUrl"]?.TrimEnd('/')
                         ?? $"{request.Scheme}://{request.Host}";
        return new
        {
            name = "Jarvis",
            description = "Self-hosted personal assistant with memory, tools, and approvals.",
            url = publicBase + "/a2a",
            version = "1.0",
            protocolVersion = "0.3.0",
            capabilities = new { streaming = false, pushNotifications = false },
            authentication = new { schemes = new[] { "bearer" } },
            defaultInputModes = new[] { "text/plain" },
            defaultOutputModes = new[] { "text/plain" },
            skills = new[]
            {
                new { id = "chat", name = "Chat", description = "Conversational assistance with memory and tools." }
            }
        };
    }

    private static string? ReadText(JsonElement body)
    {
        if (!body.TryGetProperty("params", out var parameters)) return null;
        if (parameters.TryGetProperty("message", out var message) &&
            message.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } value)
                    return value;
            }
        }
        return parameters.TryGetProperty("message", out var plain) && plain.ValueKind == JsonValueKind.String
            ? plain.GetString()
            : null;
    }

    private static string TitleFor(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length <= 60 ? (line.Length == 0 ? "A2A conversation" : "A2A · " + line) : "A2A · " + line[..57] + "…";
    }

    private static IResult JsonRpcResult(JsonElement id, object result, Guid conversationId) =>
        Results.Json(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : id.Clone(),
            ["result"] = result is ConversationTurnResult turn ? DescribeTurn(turn, conversationId) : result
        });

    private static IResult JsonRpcError(JsonElement body, int code, string message)
    {
        JsonElement? id = body.TryGetProperty("id", out var rpcId) ? rpcId.Clone() : null;
        return Results.Json(new { jsonrpc = "2.0", id, error = new { code, message } });
    }

    private static object DescribeTurn(ConversationTurnResult result, Guid conversationId) => result switch
    {
        ConversationTurnResult.Completed completed => new
        {
            kind = "message",
            role = "agent",
            parts = new[] { new { kind = "text", text = completed.Message.Content } }
        },
        ConversationTurnResult.AwaitingApproval => new
        {
            kind = "task",
            id = conversationId,
            status = new { state = "input-required", message = "Jarvis needs approval in the app before continuing." }
        },
        ConversationTurnResult.Failed failed => new { kind = "message", role = "agent", parts = new[] { new { kind = "text", text = failed.Message } } },
        ConversationTurnResult.Conflict conflict => new { kind = "message", role = "agent", parts = new[] { new { kind = "text", text = conflict.Message } } },
        _ => new { kind = "message", role = "agent", parts = new[] { new { kind = "text", text = "Jarvis could not complete that." } } }
    };

    private static RemoteAgentDto ToDto(RemoteAgentRecord agent, bool hasToken) =>
        new(agent.Id, agent.Name, agent.Url, agent.Enabled, hasToken, agent.LastUsedAt, agent.LastError, agent.CreatedAt);
}
