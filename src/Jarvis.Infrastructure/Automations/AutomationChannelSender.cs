using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Channels;
using Jarvis.Application.Integrations;
using static Jarvis.Application.Integrations.IntegrationCredentialProviders;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Infrastructure.Automations;

/// <summary>Sends owner-configured channel messages from durable automation workers.</summary>
public sealed class AutomationChannelSender(
    IChannelRepository channels,
    IIntegrationCredentialStore credentials,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAutomationChannelSender
{
    public async Task<Guid> SendPreconfiguredAsync(Guid ownerId, Guid connectionId, string recipient, string body,
        CancellationToken cancellationToken)
    {
        var connection = await channels.GetAsync(ownerId, connectionId, cancellationToken)
                         ?? throw new InvalidOperationException("Channel connection was not found.");
        if (!connection.Enabled) throw new InvalidOperationException("Channel connection is disabled.");

        var secrets = await credentials.GetSecretsAsync(ownerId,
            ChannelPrefix + connectionId.ToString("N"), cancellationToken);
        try
        {
            if (connection.Kind == ChannelKinds.WhatsApp)
                await SendWhatsAppAsync(connection, secrets, recipient, body, cancellationToken);
            else if (connection.Kind == ChannelKinds.WhatsAppLinked)
                await SendWhatsAppLinkedAsync(connection, recipient, body, cancellationToken);
            else if (connection.Kind == ChannelKinds.Signal)
                await SendSignalAsync(connection, recipient, body, cancellationToken);
            else
                throw new InvalidOperationException("Unsupported channel kind.");
            await channels.RecordOutboundAsync(connectionId, recipient, body, null, cancellationToken);
        }
        catch (Exception exception)
        {
            await channels.RecordOutboundAsync(connectionId, recipient, body, exception.Message, cancellationToken);
            throw;
        }

        return Guid.CreateVersion7();
    }

    private async Task SendWhatsAppAsync(ChannelConnectionRecord connection,
        IReadOnlyDictionary<string, string>? secrets, string recipient, string body,
        CancellationToken cancellationToken)
    {
        if (secrets is null || !secrets.TryGetValue("access_token", out var token) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The WhatsApp access token is missing.");
        var baseUrl = configuration["Channels:WhatsApp:GraphBaseUrl"] ?? "https://graph.facebook.com/v21.0";
        var http = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(connection.Account)}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = recipient.TrimStart('+'),
                type = "text",
                text = new { preview_url = false, body }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WhatsApp rejected the message ({(int)response.StatusCode}).");
    }

    private async Task SendWhatsAppLinkedAsync(ChannelConnectionRecord connection, string recipient, string body,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Channels:WhatsAppBridge:BaseUrl"]
                      ?? throw new InvalidOperationException("Channels:WhatsAppBridge:BaseUrl is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/sessions/{connection.Id:D}/send")
        {
            Content = JsonContent.Create(new { to = ChannelAddresses.Normalize(recipient), text = body })
        };
        if (configuration["Channels:WhatsAppBridge:Token"] is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The WhatsApp bridge rejected the message ({(int)response.StatusCode}).");
    }

    private async Task SendSignalAsync(ChannelConnectionRecord connection, string recipient, string body,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Channels:Signal:BaseUrl"]
                      ?? throw new InvalidOperationException("Channels:Signal:BaseUrl is not configured.");
        var http = httpClientFactory.CreateClient();
        using var response = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/v2/send", new
        {
            message = body,
            number = connection.Account,
            recipients = new[] { recipient },
            text_mode = "styled"
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"signal-cli rejected the message ({(int)response.StatusCode}).");
    }
}
