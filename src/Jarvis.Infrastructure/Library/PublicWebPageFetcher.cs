using System.Net;
using System.Net.Sockets;
using System.Text;
using Jarvis.Application.Integrations;
using Jarvis.Application.Library;

namespace Jarvis.Infrastructure.Library;

/// <summary>
/// Fetches a public HTTPS page for the library. Every connection (and every redirect hop, up to three) goes only to
/// public addresses, so a link cannot make Jarvis reach the owner's network. Only text and HTML are read, and
/// at most 2 MB of it.
/// </summary>
public sealed class PublicWebPageFetcher : IWebPageFetcher
{
    private const int MaxRedirects = 3;

    public async Task<FetchedPage> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAcceptable(current, out var uri))
                throw new WebFetchException("Jarvis can only open public https links.");

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                         DecompressionMethods.Brotli,
                MaxConnectionsPerServer = 1,
                PooledConnectionLifetime = TimeSpan.Zero,
                ConnectCallback = ConnectToPublicAddressAsync
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.8");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; JarvisLibrary/1.0)");
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    current = (location.IsAbsoluteUri ? location : new Uri(uri!, location)).ToString();
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new WebFetchException($"The site answered with an error ({(int)response.StatusCode}).");

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!(mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
                      mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase) ||
                      mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)))
                    throw new WebFetchException(
                        "That link is not a web page or text file. Upload PDFs and images as files instead.");
                if (response.Content.Headers.ContentLength > LibraryRules.MaxFetchBytes)
                    throw new WebFetchException("That page is too large to save.");

                var charset = response.Content.Headers.ContentType?.CharSet;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + read > LibraryRules.MaxFetchBytes)
                        throw new WebFetchException("That page is too large to save.");
                    buffer.Write(chunk, 0, read);
                }
                return new FetchedPage(current, mediaType, Decode(buffer.ToArray(), charset));
            }
            catch (HttpRequestException)
            {
                throw new WebFetchException("Jarvis could not reach that page.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new WebFetchException("The page took too long to answer.");
            }
        }
        throw new WebFetchException("The link redirects too many times.");
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        try
        {
            return (string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')))
                .GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public static bool IsAcceptable(string url, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            parsed.Port != 443 || !string.IsNullOrEmpty(parsed.UserInfo) || IPAddress.TryParse(parsed.Host, out _))
            return false;
        var host = parsed.IdnHost.TrimEnd('.');
        if (!host.Contains('.', StringComparison.Ordinal) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var publicAddresses = addresses.Where(McpServerEndpointValidator.IsPublic).ToArray();
        if (publicAddresses.Length == 0) throw new SocketException((int)SocketError.AccessDenied);

        Exception? lastError = null;
        foreach (var address in publicAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException) throw;
                lastError = exception;
            }
        }
        throw new HttpRequestException("Could not connect to the public host.", lastError);
    }
}
