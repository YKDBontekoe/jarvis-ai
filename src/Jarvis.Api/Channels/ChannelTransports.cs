using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Jarvis.Application.Channels;

namespace Jarvis.Api.Channels;

public sealed class ChannelOptions
{
    public string WhatsAppGraphBaseUrl { get; init; } = "https://graph.facebook.com/v21.0";
    public string? SignalBaseUrl { get; init; }
    public string? PublicBaseUrl { get; init; }

    public static ChannelOptions From(IConfiguration configuration) => new()
    {
        WhatsAppGraphBaseUrl = EmptyToNull(configuration["Channels:WhatsApp:GraphBaseUrl"])
                               ?? "https://graph.facebook.com/v21.0",
        SignalBaseUrl = EmptyToNull(configuration["Channels:Signal:BaseUrl"]),
        PublicBaseUrl = EmptyToNull(configuration["Channels:PublicBaseUrl"])
                        ?? EmptyToNull(configuration["Jarvis:PublicBaseUrl"])
    };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Sends text to one recipient on a messaging platform.</summary>
public interface IChannelTransport
{
    string Kind { get; }
    int MaxMessageLength { get; }
    Task SendAsync(ChannelConnectionRecord connection, IReadOnlyDictionary<string, string>? secrets, string recipient,
        string text, CancellationToken cancellationToken);
}

/// <summary>Meta WhatsApp Business Cloud API text messages.</summary>
public sealed class WhatsAppCloudTransport(HttpClient http, ChannelOptions options) : IChannelTransport
{
    public string Kind => ChannelKinds.WhatsApp;
    public int MaxMessageLength => 4_000;

    public async Task SendAsync(ChannelConnectionRecord connection, IReadOnlyDictionary<string, string>? secrets,
        string recipient, string text, CancellationToken cancellationToken)
    {
        if (secrets is null || !secrets.TryGetValue("access_token", out var token) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The WhatsApp access token is missing.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{options.WhatsAppGraphBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(connection.Account)}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = ChannelAddresses.Normalize(recipient).TrimStart('+'),
                type = "text",
                text = new { preview_url = true, body = text }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WhatsApp rejected the message ({(int)response.StatusCode}): " +
                                           Limit(await response.Content.ReadAsStringAsync(cancellationToken)));
    }

    private static string Limit(string value) => value.Length <= 300 ? value : value[..300];
}

/// <summary>Self-hosted signal-cli REST API (bbernhard/signal-cli-rest-api).</summary>
public sealed class SignalRestTransport(HttpClient http, ChannelOptions options) : IChannelTransport
{
    public string Kind => ChannelKinds.Signal;
    public int MaxMessageLength => 6_000;

    public async Task SendAsync(ChannelConnectionRecord connection, IReadOnlyDictionary<string, string>? secrets,
        string recipient, string text, CancellationToken cancellationToken)
    {
        var baseUrl = options.SignalBaseUrl ?? throw new InvalidOperationException("Channels:Signal:BaseUrl is not configured.");
        using var response = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/v2/send", new
        {
            message = text,
            number = connection.Account,
            recipients = new[] { ChannelAddresses.Normalize(recipient) },
            text_mode = "styled"
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"signal-cli rejected the message ({(int)response.StatusCode}).");
    }
}

public static partial class ChannelText
{
    /// <summary>Adapts assistant Markdown to what the messaging app renders.</summary>
    public static string FromMarkdown(string kind, string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Trim();
        text = Heading().Replace(text, "*$1*");
        text = Bold().Replace(text, kind == ChannelKinds.WhatsApp ? "*$1*" : "**$1**");
        text = Link().Replace(text, "$1 ($2)");
        return text;
    }

    public static IEnumerable<string> Split(string text, int maxLength)
    {
        var remaining = text;
        while (remaining.Length > maxLength)
        {
            var cut = remaining.LastIndexOf('\n', maxLength - 1);
            if (cut < maxLength / 2) cut = remaining.LastIndexOf(' ', maxLength - 1);
            if (cut < maxLength / 2) cut = maxLength;
            yield return remaining[..cut].TrimEnd();
            remaining = remaining[cut..].TrimStart();
        }
        if (remaining.Length > 0) yield return remaining;
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex Link();
}
