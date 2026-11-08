using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Jarvis.Application.Channels;

namespace Jarvis.Api.Channels;

public sealed class ChannelOptions
{
    public string WhatsAppGraphBaseUrl { get; init; } = "https://graph.facebook.com/v21.0";
    public string? SignalBaseUrl { get; init; }
    public string? WhatsAppBridgeUrl { get; init; }
    public string? WhatsAppBridgeToken { get; init; }
    public string? PublicBaseUrl { get; init; }

    public static ChannelOptions From(IConfiguration configuration) => new()
    {
        WhatsAppGraphBaseUrl = EmptyToNull(configuration["Channels:WhatsApp:GraphBaseUrl"])
                               ?? "https://graph.facebook.com/v21.0",
        SignalBaseUrl = EmptyToNull(configuration["Channels:Signal:BaseUrl"]),
        WhatsAppBridgeUrl = EmptyToNull(configuration["Channels:WhatsAppBridge:BaseUrl"]),
        WhatsAppBridgeToken = EmptyToNull(configuration["Channels:WhatsAppBridge:Token"]),
        PublicBaseUrl = EmptyToNull(configuration["Channels:PublicBaseUrl"])
                        ?? EmptyToNull(configuration["Jarvis:PublicBaseUrl"])
    };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A media file downloaded from a messaging platform.</summary>
public sealed record DownloadedMedia(byte[] Content, string ContentType);

/// <summary>Sends text to one recipient on a messaging platform, and downloads media that arrived with a message.</summary>
public interface IChannelTransport
{
    string Kind { get; }
    int MaxMessageLength { get; }
    Task SendAsync(ChannelConnectionRecord connection, IReadOnlyDictionary<string, string>? secrets, string recipient,
        string text, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads one media item with the connection's own credentials, or returns null when this platform has none
    /// to give or the item is larger than <paramref name="maxBytes"/>.
    /// </summary>
    Task<DownloadedMedia?> FetchMediaAsync(ChannelConnectionRecord connection,
        IReadOnlyDictionary<string, string>? secrets, string mediaId, long maxBytes,
        CancellationToken cancellationToken) => Task.FromResult<DownloadedMedia?>(null);
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

    public async Task<DownloadedMedia?> FetchMediaAsync(ChannelConnectionRecord connection,
        IReadOnlyDictionary<string, string>? secrets, string mediaId, long maxBytes,
        CancellationToken cancellationToken)
    {
        if (secrets is null || !secrets.TryGetValue("access_token", out var token) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The WhatsApp access token is missing.");
        // Step one: the media id resolves to a short-lived URL (the id is the platform's, never a URL we are given).
        using var lookup = new HttpRequestMessage(HttpMethod.Get,
            $"{options.WhatsAppGraphBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(mediaId)}");
        lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var lookupResponse = await http.SendAsync(lookup, cancellationToken);
        if (!lookupResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"WhatsApp could not find that media ({(int)lookupResponse.StatusCode}).");
        using var document = System.Text.Json.JsonDocument.Parse(
            await lookupResponse.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
        var mime = root.TryGetProperty("mime_type", out var mimeElement) ? mimeElement.GetString() : null;
        if (root.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var bytes) && bytes > maxBytes)
            return null;
        if (!IsTrustedMediaUrl(url, options.WhatsAppGraphBaseUrl))
            throw new InvalidOperationException("WhatsApp returned a media address Jarvis does not trust.");

        // Step two: the file itself, with the same token, read no further than the limit.
        using var download = new HttpRequestMessage(HttpMethod.Get, url);
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(download, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WhatsApp could not deliver that media ({(int)response.StatusCode}).");
        if (response.Content.Headers.ContentLength > maxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        var contentType = mime ?? response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new DownloadedMedia(buffer.ToArray(), contentType);
    }

    /// <summary>
    /// The media URL must be HTTPS, or sit on the configured Graph host (which is plain HTTP only for a test double),
    /// so a hostile or garbled lookup response cannot make Jarvis fetch an internal address with its token.
    /// </summary>
    internal static bool IsTrustedMediaUrl(string? url, string graphBaseUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var media) || !string.IsNullOrEmpty(media.UserInfo)) return false;
        if (media.Scheme == Uri.UriSchemeHttps)
            return !media.IsLoopback && !System.Net.IPAddress.TryParse(media.Host, out _);
        return Uri.TryCreate(graphBaseUrl, UriKind.Absolute, out var graph) && media.Scheme == Uri.UriSchemeHttp &&
               graph.Scheme == Uri.UriSchemeHttp && media.Host == graph.Host && media.Port == graph.Port;
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

    public async Task<DownloadedMedia?> FetchMediaAsync(ChannelConnectionRecord connection,
        IReadOnlyDictionary<string, string>? secrets, string mediaId, long maxBytes,
        CancellationToken cancellationToken)
    {
        var baseUrl = options.SignalBaseUrl ?? throw new InvalidOperationException("Channels:Signal:BaseUrl is not configured.");
        using var response = await http.GetAsync($"{baseUrl.TrimEnd('/')}/v1/attachments/{Uri.EscapeDataString(mediaId)}",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"signal-cli could not deliver that attachment ({(int)response.StatusCode}).");
        if (response.Content.Headers.ContentLength > maxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return new DownloadedMedia(buffer.ToArray(),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }
}

public static partial class ChannelText
{
    /// <summary>Adapts assistant Markdown to what the messaging app renders.</summary>
    public static string FromMarkdown(string kind, string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Trim();
        text = Heading().Replace(text, "*$1*");
        text = Bold().Replace(text, ChannelKinds.IsWhatsApp(kind) ? "*$1*" : "**$1**");
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
