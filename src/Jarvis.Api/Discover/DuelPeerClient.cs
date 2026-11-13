using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Jarvis.Application.Agents;
using Jarvis.Application.Discover;
using Jarvis.Application.Integrations;

namespace Jarvis.Api.Discover;

/// <summary>
/// Asks a friend's Jarvis for a duel's terms or score over Agent2Agent JSON-RPC, with the token the owner saved for
/// that agent. Only the duel code and our side's role are sent; only metric, dates or one number come back.
/// </summary>
internal sealed class DuelPeerClient(
    IRemoteAgentRepository agents,
    IIntegrationCredentialStore credentials,
    IHttpClientFactory httpClients) : IDuelPeerClient
{
    public const string TermsMethod = "jarvis/duel.terms";
    public const string ScoreMethod = "jarvis/duel.score";

    public async Task<PeerDuelTerms> GetTermsAsync(Guid ownerId, Guid remoteAgentId, string code,
        CancellationToken cancellationToken)
    {
        var result = await CallAsync(ownerId, remoteAgentId, TermsMethod, new { code }, cancellationToken);
        if (!result.TryGetProperty("metric", out var metric) || metric.GetString() is not { } metricName ||
            !TryDate(result, "startsOn", out var startsOn) || !TryDate(result, "endsOn", out var endsOn))
            throw new DuelPeerException("Your friend’s Jarvis sent something unexpected.");
        return new PeerDuelTerms(metricName, startsOn, endsOn);
    }

    public async Task<int> GetScoreAsync(Guid ownerId, Guid remoteAgentId, string code, string myRole,
        CancellationToken cancellationToken)
    {
        var result = await CallAsync(ownerId, remoteAgentId, ScoreMethod, new { code, role = myRole },
            cancellationToken);
        return result.TryGetProperty("score", out var score) && score.TryGetInt32(out var value)
            ? value
            : throw new DuelPeerException("Your friend’s Jarvis sent something unexpected.");
    }

    private async Task<JsonElement> CallAsync(Guid ownerId, Guid remoteAgentId, string method, object parameters,
        CancellationToken cancellationToken)
    {
        var peer = await agents.GetAsync(ownerId, remoteAgentId, cancellationToken)
                   ?? throw new DuelPeerException("That Jarvis is no longer under Agents.");
        if (!peer.Enabled) throw new DuelPeerException($"{peer.Name} is paused under Agents.");
        var secrets = await credentials.GetSecretsAsync(ownerId,
            IntegrationCredentialProviders.RemoteAgentPrefix + peer.Id.ToString("N"), cancellationToken);
        if (secrets?.GetValueOrDefault("token") is not { Length: > 0 } token)
            throw new DuelPeerException($"Save {peer.Name}’s token under Agents first.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, peer.Url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = Guid.CreateVersion7().ToString("N"),
                method,
                @params = parameters
            });
            using var response = await httpClients.CreateClient("a2a").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await agents.RecordUseAsync(ownerId, peer.Id, $"HTTP {(int)response.StatusCode}", cancellationToken);
                throw new DuelPeerException($"{peer.Name} refused ({(int)response.StatusCode}).");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new DuelPeerException(error.TryGetProperty("code", out var code) && code.TryGetInt32(out var c) &&
                                            c == -32004
                    ? $"{peer.Name} has no duel with that code."
                    : $"{peer.Name} does not support duels yet.");
            await agents.RecordUseAsync(ownerId, peer.Id, null, cancellationToken);
            return root.TryGetProperty("result", out var result)
                ? result.Clone()
                : throw new DuelPeerException($"{peer.Name} sent something unexpected.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await agents.RecordUseAsync(ownerId, peer.Id, "unreachable", cancellationToken);
            throw new DuelPeerException($"{peer.Name} is unreachable right now.");
        }
    }

    private static bool TryDate(JsonElement root, string name, out DateOnly value)
    {
        value = default;
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String &&
               DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out value);
    }
}
