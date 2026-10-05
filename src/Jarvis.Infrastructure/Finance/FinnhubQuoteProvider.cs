using System.Globalization;
using System.Text.Json;
using Jarvis.Application.Finance;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Infrastructure.Finance;

/// <summary>
/// Latest prices from Finnhub. Only registered when <c>Finance:Quotes:ApiKey</c> is set. The key goes in a request
/// header, never in a URL, so it cannot end up in logs.
/// </summary>
public sealed class FinnhubQuoteProvider(HttpClient http, IConfiguration configuration, TimeProvider? timeProvider = null)
    : IQuoteProvider
{
    internal const string ApiKeySetting = "Finance:Quotes:ApiKey";
    private const int MaxSymbolsPerRefresh = 40;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration[ApiKeySetting]);

    public async Task<IReadOnlyDictionary<string, Quote>> GetQuotesAsync(IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Quote>(StringComparer.Ordinal);
        var key = configuration[ApiKeySetting];
        if (string.IsNullOrWhiteSpace(key)) return result;

        foreach (var symbol in symbols.Take(MaxSymbolsPerRefresh))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(symbol)}");
            request.Headers.Add("X-Finnhub-Token", key);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            var price = ParsePrice(await response.Content.ReadAsStringAsync(cancellationToken));
            if (price is > 0) result[symbol] = new Quote(symbol, price.Value, clock.GetUtcNow());
        }
        return result;
    }

    /// <summary>Reads the current price ("c") from a quote response; null when the symbol is unknown (price 0).</summary>
    internal static decimal? ParsePrice(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.Number &&
                   c.TryGetDecimal(out var price) && price > 0
                ? price
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
