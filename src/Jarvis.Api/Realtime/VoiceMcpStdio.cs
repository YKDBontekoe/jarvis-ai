using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jarvis.Api.Realtime;

internal static class VoiceMcpStdio
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClientFromEnvironment();
        using var input = Console.OpenStandardInput();
        using var reader = new StreamReader(input);
        using var output = Console.OpenStandardOutput();
        using var writer = new StreamWriter(output) { AutoFlush = true };
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) return;
            JsonElement message;
            try
            {
                message = JsonDocument.Parse(line).RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (message.ValueKind != JsonValueKind.Object) continue;
            try
            {
                var (response, stop) = await HandleAsync(message, client, cancellationToken);
                if (response is not null)
                    await writer.WriteLineAsync(response.ToJsonString(JsonOptions).AsMemory(), cancellationToken);
                if (stop) return;
            }
            catch
            {
                if (message.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                {
                    var error = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = JsonNode.Parse(id.GetRawText()),
                        ["error"] = new JsonObject { ["code"] = -32603, ["message"] = "Internal error" }
                    };
                    await writer.WriteLineAsync(error.ToJsonString(JsonOptions).AsMemory(), cancellationToken);
                }
            }
        }
    }

    public static string McpServerConfig(string command, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var parts = new List<string>
        {
            "command=" + JsonSerializer.Serialize(command),
            "args=" + JsonSerializer.Serialize(arguments)
        };
        if (env is { Count: > 0 })
        {
            var body = string.Join(',', env.Select(pair => pair.Key + "=" + JsonSerializer.Serialize(pair.Value)));
            parts.Add("env={" + body + "}");
        }

        return "mcp_servers={jarvis={" + string.Join(',', parts) + "}}";
    }

    public static (string Command, string[] Arguments) ResolveLaunch()
    {
        var entry = Environment.ProcessPath;
        var assembly = typeof(VoiceMcpStdio).Assembly.Location;
        if (string.IsNullOrWhiteSpace(entry) ||
            string.Equals(Path.GetFileNameWithoutExtension(entry), "dotnet", StringComparison.OrdinalIgnoreCase))
            return ("dotnet", [assembly, "voice-mcp"]);
        return (entry, ["voice-mcp"]);
    }

    internal static VoiceMcpClient CreateClientFromEnvironment(IReadOnlyDictionary<string, string>? environ = null)
    {
        string Read(string key)
        {
            if (environ is not null && environ.TryGetValue(key, out var value)) return value;
            return Environment.GetEnvironmentVariable(key) ?? "";
        }

        var apiUrl = Read("JARVIS_INTERNAL_API_URL").TrimEnd('/');
        var secret = Read("VOICE_WORKER_SECRET");
        var conversationId = Read("JARVIS_VOICE_CONVERSATION_ID");
        var ownerId = Read("JARVIS_VOICE_OWNER_ID");
        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(ownerId))
            throw new InvalidOperationException("The Jarvis voice tool is missing its server configuration.");
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Jarvis-Voice-Secret", secret);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new VoiceMcpClient(http, $"{apiUrl}/api/v1/voice/internal/{conversationId}", ownerId);
    }

    internal static async Task<(JsonObject? Response, bool Stop)> HandleAsync(JsonElement message, VoiceMcpClient client,
        CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            return (null, false);
        var method = methodElement.GetString();
        if (!message.TryGetProperty("id", out var id) || id.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return (null, method == "exit");

        JsonNode result;
        if (method == "initialize")
        {
            result = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "jarvis", ["version"] = "1.0.0" }
            };
        }
        else if (method == "ping")
            result = new JsonObject();
        else if (method == "tools/list")
            result = new JsonObject { ["tools"] = await client.ListToolsAsync(cancellationToken) };
        else if (method == "tools/call")
            result = await client.CallToolAsync(message, cancellationToken);
        else if (method == "shutdown")
            result = new JsonObject();
        else
        {
            return (new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id.GetRawText()),
                ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" }
            }, false);
        }

        return (new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["result"] = result
        }, method == "shutdown");
    }
}

internal sealed class VoiceMcpClient(HttpClient http, string baseUrl, string ownerId) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"{baseUrl}/session?ownerId={Uri.EscapeDataString(ownerId)}",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var tools = new JsonArray();
            if (!document.RootElement.TryGetProperty("tools", out var listed) || listed.ValueKind != JsonValueKind.Array)
                return tools;
            foreach (var descriptor in listed.EnumerateArray())
            {
                if (descriptor.ValueKind != JsonValueKind.Object) continue;
                var name = descriptor.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var schema = descriptor.TryGetProperty("inputSchema", out var schemaElement) &&
                             schemaElement.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(schemaElement.GetRawText())
                    : new JsonObject { ["type"] = "object", ["additionalProperties"] = true };
                var description = descriptor.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString() ?? ""
                    : "";
                tools.Add(new JsonObject
                {
                    ["name"] = name,
                    ["description"] = description,
                    ["inputSchema"] = schema
                });
            }

            return tools;
        }
        catch
        {
            return [];
        }
    }

    public async Task<JsonObject> CallToolAsync(JsonElement message, CancellationToken cancellationToken)
    {
        JsonObject Failed(string text) => new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = true
        };

        var parameters = message.TryGetProperty("params", out var paramsElement) &&
                         paramsElement.ValueKind == JsonValueKind.Object
            ? paramsElement
            : default;
        var name = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Failed("This Jarvis voice tool call is invalid.");
        JsonElement arguments = default;
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var args) &&
            args.ValueKind == JsonValueKind.Object)
            arguments = args;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1200));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/tools/{Uri.EscapeDataString(name)}")
            {
                Content = JsonContent.Create(new
                {
                    ownerId,
                    arguments = arguments.ValueKind == JsonValueKind.Object
                        ? JsonSerializer.Deserialize<JsonElement>(arguments.GetRawText())
                        : JsonSerializer.Deserialize<JsonElement>("{}")
                }, options: JsonOptions)
            };
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var text = document.RootElement.TryGetProperty("result", out var result) &&
                       result.ValueKind == JsonValueKind.String
                ? result.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(text)) text = "The Jarvis tool finished with no text.";
            var isError = document.RootElement.TryGetProperty("isError", out var errorFlag) &&
                          errorFlag.ValueKind == JsonValueKind.True;
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["isError"] = isError
            };
        }
        catch
        {
            return Failed("Jarvis could not complete this request. Check the Jarvis app.");
        }
    }

    public void Dispose() => http.Dispose();
}
