using System.Collections.Concurrent;
using System.Text.Json;
using Jarvis.Application.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Integrations;

/// <summary>
/// Reads the official MCP registry (registry.modelcontextprotocol.io). Only the latest version of each server
/// is listed, and only servers Jarvis can run. Responses are cached for ten minutes.
/// </summary>
public sealed partial class OfficialMcpRegistryCatalog(HttpClient http, IConfiguration configuration,
    ILogger<OfficialMcpRegistryCatalog> logger) : IMcpCatalog
{
    public const string DefaultBaseUrl = "https://registry.modelcontextprotocol.io";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private const int MaxCachedResponses = 200;
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, object Value)> Cache = new();

    public async Task<McpCatalogPage> SearchAsync(string? query, string? cursor, int limit,
        CancellationToken cancellationToken)
    {
        var search = query?.Trim() ?? "";
        if (search.Length > 100) search = search[..100];
        // The registry filters before Jarvis drops servers it cannot run, so ask for more than one page needs.
        var pageSize = Math.Clamp(limit, 1, 50);
        var path = $"/v0.1/servers?version=latest&limit={pageSize * 2}" +
                   (search.Length > 0 ? "&search=" + Uri.EscapeDataString(search) : "") +
                   (string.IsNullOrWhiteSpace(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor.Trim()));
        return await CachedAsync(path, async () =>
        {
            using var document = await GetJsonAsync(path, cancellationToken)
                                 ?? throw new McpCatalogUnavailableException("The app catalog did not answer.");
            var root = document.RootElement;
            var servers = new List<McpCatalogEntry>();
            if (root.TryGetProperty("servers", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!IsActive(item) || !item.TryGetProperty("server", out var server)) continue;
                    if (McpRegistryMapper.Map(server) is { } entry && servers.All(existing => existing.Name != entry.Name))
                        servers.Add(entry);
                }
            }
            var ranked = McpRegistryMapper.Rank(servers, search).Take(pageSize).ToArray();
            string? next = null;
            if (root.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("nextCursor", out var nextCursor) &&
                nextCursor.ValueKind == JsonValueKind.String)
                next = nextCursor.GetString();
            return new McpCatalogPage(ranked, next);
        });
    }

    public async Task<McpCatalogEntry?> GetAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return null;
        var path = $"/v0.1/servers/{Uri.EscapeDataString(name.Trim())}/versions/latest";
        var entry = await CachedAsync<McpCatalogEntry?>(path, async () =>
        {
            using var document = await GetJsonAsync(path, cancellationToken);
            if (document is null || !IsActive(document.RootElement) ||
                !document.RootElement.TryGetProperty("server", out var server))
                return null;
            return McpRegistryMapper.Map(server);
        });
        return entry is not null && entry.Name == name.Trim() ? entry : null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Mcp:Registry:BaseUrl"] ?? DefaultBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new McpCatalogUnavailableException("The app catalog is turned off on this Jarvis server.");
        try
        {
            using var response = await http.GetAsync(baseUrl.TrimEnd('/') + path, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                LogRegistryStatus(logger, (int)response.StatusCode);
                throw new McpCatalogUnavailableException("The app catalog is not answering right now. Try again soon.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
                                              TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogRegistryFailure(logger, exception.GetType().Name);
            throw new McpCatalogUnavailableException("The app catalog is not answering right now. Try again soon.");
        }
    }

    private static bool IsActive(JsonElement item) =>
        !item.TryGetProperty("_meta", out var meta) ||
        !meta.TryGetProperty("io.modelcontextprotocol.registry/official", out var official) ||
        !official.TryGetProperty("status", out var status) ||
        status.ValueKind != JsonValueKind.String || status.GetString() == "active";

    private static async Task<T> CachedAsync<T>(string key, Func<Task<T>> load)
    {
        var now = DateTimeOffset.UtcNow;
        if (Cache.TryGetValue(key, out var cached) && cached.Expires > now) return (T)cached.Value;
        var value = await load();
        if (Cache.Count >= MaxCachedResponses)
        {
            foreach (var stale in Cache.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                Cache.TryRemove(stale, out _);
            if (Cache.Count >= MaxCachedResponses) Cache.Clear();
        }
        if (value is not null) Cache[key] = (now.Add(CacheFor), value);
        return value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP registry returned HTTP {StatusCode}.")]
    private static partial void LogRegistryStatus(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP registry request failed: {ErrorType}.")]
    private static partial void LogRegistryFailure(ILogger logger, string errorType);
}
