using System.Net;
using System.Text;
using Jarvis.Application.Reading;

namespace Jarvis.Workflows.Reading;

/// <summary>
/// Downloads a saved link for the reading list. Every hop, redirects included, goes through
/// <see cref="ReadingUrls.Normalize"/> (the isolated browser's blocked addresses and more) and connects only to public
/// IP addresses, so DNS tricks cannot reach the local network. No cookies, credentials or proxy are used.
/// </summary>
public sealed class ReadingPageFetcher : IReadingPageFetcher
{
    private const int MaxResponseBytes = 3 * 1024 * 1024;
    private const int MaxRedirects = 5;

    public async Task<FetchedPage> FetchAsync(string url, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = PublicJsonMetricReader.ConnectToPublicAddressAsync
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var current = Validate(url);
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.8");
            request.Headers.AcceptLanguage.ParseAdd("en;q=0.8,nl;q=0.7,*;q=0.5");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Jarvis-ReadingList/1.0)");
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new ReadingFetchException("The site could not be reached.", false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ReadingFetchException("The site took too long to answer.", false);
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (response.Headers.Location is not { } location)
                        throw new ReadingFetchException("The site redirected without a destination.", true);
                    current = Validate(new Uri(current, location).AbsoluteUri);
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    throw new ReadingFetchException("The page no longer exists.", true);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or
                    HttpStatusCode.PaymentRequired)
                    throw new ReadingFetchException("The site does not let Jarvis read this page (sign-in or paywall).", true);
                if (!response.IsSuccessStatusCode)
                    throw new ReadingFetchException($"The site answered with error {(int)response.StatusCode}.",
                        (int)response.StatusCode is >= 400 and < 500 and not 408 and not 429);

                var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                var isHtml = mediaType is null or "text/html" or "application/xhtml+xml";
                if (!isHtml && mediaType != "text/plain")
                    throw new ReadingFetchException("Only web pages can be summarized, not files like PDFs or images.", true);
                if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                    throw new ReadingFetchException("The page is too large to read.", true);

                var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
                var text = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
                return isHtml
                    ? ReadingPageText.FromHtml(current.AbsoluteUri, text)
                    : ReadingPageText.FromPlainText(current.AbsoluteUri, text);
            }
        }
        throw new ReadingFetchException("The site redirected too many times.", true);
    }

    private static Uri Validate(string url)
    {
        try
        {
            return new Uri(ReadingUrls.Normalize(url));
        }
        catch (ArgumentException exception)
        {
            throw new ReadingFetchException(exception.Message, true);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var bounded = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (bounded.Length + count > MaxResponseBytes)
                throw new ReadingFetchException("The page is too large to read.", true);
            await bounded.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return bounded.ToArray();
    }

    public static string Decode(byte[] bytes, string? charset)
    {
        var encoding = TryEncoding(charset) ?? TryEncoding(SniffCharset(bytes)) ?? Encoding.UTF8;
        return encoding.GetString(bytes);
    }

    private static Encoding? TryEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return Encoding.GetEncoding(name.Trim().Trim('"')); }
        catch (ArgumentException) { return null; }
    }

    private static string? SniffCharset(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var match = System.Text.RegularExpressions.Regex.Match(head, @"charset\s*=\s*[""']?([A-Za-z0-9_\-]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }
}
