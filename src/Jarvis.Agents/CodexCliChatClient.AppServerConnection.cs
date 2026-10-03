using System.Text;
using System.Text.Json;

namespace Jarvis.Agents;

/// <summary>JSON-RPC over the Codex app-server process's stdio.</summary>
public sealed partial class CodexCliChatClient
{
    private sealed class AppServerConnection(StreamWriter writer, StreamReader reader, Task<string> stderrTask,
        CodexAccess access, string scratch)
    {
        private int _requestId;
        private CodexTokenUsage? _latestUsage;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var requestId = await WriteAsync("initialize", new { clientInfo = new { name = "jarvis", version = "1.0.0" } }, cancellationToken);
            using var response = await ReadResponseAsync(requestId, cancellationToken);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { method = "initialized", @params = new { } }).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        public async Task<AvailableModel[]> ListModelsAsync(CancellationToken cancellationToken)
        {
            var models = new List<AvailableModel>();
            string? cursor = null;
            do
            {
                var requestId = await WriteAsync("model/list", new { includeHidden = true, limit = 100, cursor }, cancellationToken);
                using var response = await ReadResponseAsync(requestId, cancellationToken);
                var result = response.RootElement.GetProperty("result");
                foreach (var item in result.GetProperty("data").EnumerateArray())
                {
                    var modelId = item.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : null;
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(modelId)) modelId = id;
                    if (string.IsNullOrWhiteSpace(modelId)) continue;
                    var modalities = item.TryGetProperty("inputModalities", out var inputModalities) &&
                                     inputModalities.ValueKind == JsonValueKind.Array
                        ? inputModalities.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                            .Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var isDefault = item.TryGetProperty("isDefault", out var defaultElement) &&
                                    defaultElement.ValueKind == JsonValueKind.True;
                    models.Add(new AvailableModel(modelId, id, modalities, isDefault));
                }
                cursor = result.TryGetProperty("nextCursor", out var nextCursor) &&
                         nextCursor.ValueKind == JsonValueKind.String ? nextCursor.GetString() : null;
            }
            while (!string.IsNullOrWhiteSpace(cursor) && models.Count < 1_000);
            return models.ToArray();
        }

        public async Task<string> StartThreadAsync(string scratch, string modelId, CancellationToken cancellationToken)
        {
            var requestId = await WriteAsync("thread/start", new
            {
                approvalPolicy = "never",
                sandbox = access.ThreadSandbox,
                cwd = scratch,
                ephemeral = true,
                model = modelId
            }, cancellationToken);
            using var response = await ReadResponseAsync(requestId, cancellationToken);
            return response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Codex CLI app-server returned no thread identifier.");
        }

        public async Task<CodexTurnMetrics> RunTurnAsync(string threadId, PromptPayload prompt, string modelId,
            string? reasoningEffort, Action<string?, string> onDelta, CancellationToken cancellationToken,
            Action<string, string>? onNativeTool = null)
        {
            _latestUsage = null;
            var webSearches = 0;
            // Tool validation is performed by the outer client after parsing the structured response.
            // The prompt itself contains the exact allowlisted function definitions.
            var requestId = ++_requestId;
            await WriteAsync("turn/start", new
            {
                threadId,
                model = modelId,
                effort = reasoningEffort,
                approvalPolicy = "never",
                sandboxPolicy = access.TurnSandboxPolicy(scratch),
                input = new object[] { new { type = "text", text = prompt.Text } }
                    .Concat(prompt.Images.Select(url => (object)new { type = "image", url, detail = "auto" })).ToArray(),
                outputSchema = OutputSchema
            }, cancellationToken, requestId);

            while (true)
            {
                using var message = await ReadMessageAsync(cancellationToken);
                var root = message.RootElement;
                if (root.TryGetProperty("id", out var id) && id.TryGetInt32(out var responseId) && responseId == requestId)
                {
                    if (root.TryGetProperty("error", out var error))
                        throw new InvalidOperationException("Codex CLI app-server rejected the model turn: " + GetRpcError(error));
                    continue;
                }

                if (!root.TryGetProperty("method", out var method)) continue;
                var methodName = method.GetString();
                if (methodName == "thread/tokenUsage/updated" && root.TryGetProperty("params", out var usageParameters) &&
                    usageParameters.TryGetProperty("tokenUsage", out var threadUsage) &&
                    threadUsage.TryGetProperty("last", out var lastUsage))
                {
                    _latestUsage = new CodexTokenUsage(ReadTokenCount(lastUsage, "inputTokens"),
                        ReadTokenCount(lastUsage, "outputTokens"), ReadTokenCount(lastUsage, "cachedInputTokens"),
                        ReadTokenCount(lastUsage, "reasoningOutputTokens"));
                }
                else if (methodName == "item/agentMessage/delta" && root.TryGetProperty("params", out var parameters) &&
                    parameters.TryGetProperty("delta", out var delta))
                {
                    var itemId = parameters.TryGetProperty("itemId", out var item) && item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : null;
                    onDelta(itemId, delta.GetString() ?? string.Empty);
                }
                else if ((methodName == "item/completed" || methodName == "item/started") &&
                         IsNativeWebSearchNotification(methodName, root))
                {
                    if (NativeItemId(root) is { } itemId)
                        onNativeTool?.Invoke("websearch-" + itemId,
                            methodName == "item/started" ? "started" : "completed");
                    if (methodName == "item/completed")
                    {
                        WebSearchActions.Add(1);
                        webSearches++;
                    }
                }
                else if (IsNativeWebSearchNotification(methodName, root) &&
                         methodName is not ("item/completed" or "item/started"))
                {
                    WebSearchActions.Add(1);
                    webSearches++;
                }
                else if (methodName == "turn/completed")
                {
                    var turn = root.GetProperty("params").GetProperty("turn");
                    var status = turn.GetProperty("status").GetString();
                    if (status != "completed")
                    {
                        var error = turn.TryGetProperty("error", out var turnError) &&
                                    turnError.TryGetProperty("message", out var errorMessage)
                            ? errorMessage.GetString()
                            : null;
                        throw new InvalidOperationException("Codex CLI model turn " + (status ?? "failed") +
                            (string.IsNullOrWhiteSpace(error) ? "." : ": " + Limit(error, 2_000)));
                    }
                    return new CodexTurnMetrics(_latestUsage, webSearches);
                }
            }
        }

        private static long ReadTokenCount(JsonElement usage, string propertyName) =>
            usage.TryGetProperty(propertyName, out var count) && count.TryGetInt64(out var value) && value >= 0
                ? value : 0;

        private async Task<int> WriteAsync(string method, object parameters, CancellationToken cancellationToken,
            int? requestId = null)
        {
            var id = requestId ?? ++_requestId;
            var line = JsonSerializer.Serialize(new { method, id, @params = parameters }, JsonOptions);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return id;
        }

        private async Task<JsonDocument> ReadResponseAsync(int requestId, CancellationToken cancellationToken)
        {
            while (true)
            {
                var message = await ReadMessageAsync(cancellationToken);
                if (!message.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt32(out var responseId) || responseId != requestId)
                {
                    message.Dispose();
                    continue;
                }
                if (message.RootElement.TryGetProperty("error", out var error))
                {
                    var reason = GetRpcError(error);
                    message.Dispose();
                    throw new InvalidOperationException("Codex CLI app-server request failed: " + reason);
                }
                return message;
            }
        }

        private async Task<JsonDocument> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                string stderr;
                try
                {
                    stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (TimeoutException)
                {
                    stderr = "";
                }
                throw new InvalidOperationException("Codex CLI app-server exited unexpectedly: " + Limit(stderr.Trim(), 2_000));
            }
            return JsonDocument.Parse(line);
        }

        private static string GetRpcError(JsonElement error) =>
            error.TryGetProperty("message", out var message) ? Limit(message.GetString() ?? "unknown error", 2_000) : "unknown error";
    }

    private sealed class StructuredTextStreamDecoder
    {
        private readonly StringBuilder _raw = new();
        private string _emitted = string.Empty;
        private string _earlier = string.Empty;
        private bool _separate;
        private bool _isText;

        /// <summary>
        /// Starts decoding the next agent message of the same turn. Text already streamed stays shown: a
        /// message that repeats or extends it only streams what is new, and a different message follows it
        /// after a blank line.
        /// </summary>
        public void StartNextMessage()
        {
            _raw.Clear();
            _isText = false;
            _earlier = _emitted;
            _separate = false;
        }

        public string Append(string delta)
        {
            _raw.Append(delta);
            var content = TryReadRootString("type");
            if (content.IsComplete && content.Value == "text") _isText = true;
            if (!_isText) return string.Empty;

            content = TryReadRootString("text");
            if (content.Value is null) return string.Empty;
            var shown = Shown(content.Value);
            if (shown is null) return string.Empty;
            if (!shown.StartsWith(_emitted, StringComparison.Ordinal))
                throw new InvalidOperationException("Codex CLI streamed a non-prefix assistant response.");
            var next = shown[_emitted.Length..];
            _emitted = shown;
            return next;
        }

        public string Complete()
        {
            if (!_isText) return string.Empty;
            var content = TryReadRootString("text");
            if (content.Value is null || Shown(content.Value) is not { } shown ||
                !shown.StartsWith(_emitted, StringComparison.Ordinal))
                return string.Empty;
            var next = shown[_emitted.Length..];
            _emitted = shown;
            return next;
        }

        /// <summary>The full streamed text once this message reads as <paramref name="text"/>, or null while it
        /// still only repeats text that was already shown.</summary>
        private string? Shown(string text)
        {
            if (_earlier.Length == 0) return text;
            if (!_separate)
            {
                if (_earlier.StartsWith(text, StringComparison.Ordinal)) return null;
                if (text.StartsWith(_earlier, StringComparison.Ordinal)) return text;
                _separate = true;
            }
            return _earlier + "\n\n" + text;
        }

        private (string? Value, bool IsComplete) TryReadRootString(string propertyName)
        {
            var text = _raw.ToString();
            var index = 0;
            SkipWhitespace(text, ref index);
            if (index >= text.Length || text[index++] != '{') return (null, false);
            while (index < text.Length)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] == '}') return (null, false);
                var key = ReadJsonString(text, ref index);
                if (!key.IsComplete) return (null, false);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index++] != ':') return (null, false);
                SkipWhitespace(text, ref index);
                if (key.Value == propertyName)
                {
                    if (index >= text.Length || text[index] != '"') return (null, false);
                    return ReadJsonString(text, ref index);
                }
                if (!SkipJsonValue(text, ref index)) return (null, false);
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ',') index++;
                else if (index < text.Length && text[index] == '}') return (null, false);
                else return (null, false);
            }
            return (null, false);
        }

        private static (string? Value, bool IsComplete) ReadJsonString(string input, ref int index)
        {
            if (index >= input.Length || input[index] != '"') return (null, false);
            var openingQuote = index++;
            var escaped = false;
            for (var cursor = index; cursor < input.Length; cursor++)
            {
                var character = input[cursor];
                if (escaped)
                {
                    if (character == 'u')
                    {
                        if (cursor + 4 >= input.Length || !IsHex(input.AsSpan(cursor + 1, Math.Min(4, input.Length - cursor - 1))))
                            return DecodePrefix(input, openingQuote + 1, cursor);
                        cursor += 4;
                    }
                    else if (character is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't'))
                    {
                        return (null, false);
                    }
                    escaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (character == '"')
                {
                    var raw = input[(openingQuote + 1)..cursor];
                    index = cursor + 1;
                    return (JsonSerializer.Deserialize<string>('"' + raw + '"'), true);
                }
            }
            return DecodePrefix(input, openingQuote + 1, input.Length);
        }

        private static (string? Value, bool IsComplete) DecodePrefix(string input, int start, int end)
        {
            for (var cursor = start; cursor < end;)
            {
                if (input[cursor] != '\\')
                {
                    if (input[cursor] == '"')
                    {
                        end = cursor;
                        break;
                    }
                    cursor++;
                    continue;
                }

                if (cursor + 1 >= end)
                {
                    end = cursor;
                    break;
                }
                if (input[cursor + 1] == 'u')
                {
                    if (cursor + 6 > end)
                    {
                        end = cursor;
                        break;
                    }
                    if (!IsHex(input.AsSpan(cursor + 2, 4))) return (null, false);
                    cursor += 6;
                }
                else if (input[cursor + 1] is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')
                {
                    cursor += 2;
                }
                else
                {
                    return (null, false);
                }
            }
            var raw = input[start..end];
            try { return (JsonSerializer.Deserialize<string>('"' + raw + '"'), false); }
            catch (JsonException) { return (null, false); }
        }

        private static bool SkipJsonValue(string input, ref int index)
        {
            if (index >= input.Length) return false;
            if (input[index] == '"')
            {
                var parsed = ReadJsonString(input, ref index);
                return parsed.IsComplete;
            }
            if (input[index] is '{' or '[')
            {
                var opening = input[index++];
                var closing = opening == '{' ? '}' : ']';
                var depth = 1;
                var quoted = false;
                var escaped = false;
                while (index < input.Length)
                {
                    var character = input[index++];
                    if (quoted)
                    {
                        if (escaped) escaped = false;
                        else if (character == '\\') escaped = true;
                        else if (character == '"') quoted = false;
                        continue;
                    }
                    if (character == '"') quoted = true;
                    else if (character == opening) depth++;
                    else if (character == closing && --depth == 0) return true;
                }
                return false;
            }
            var start = index;
            while (index < input.Length && input[index] is not ',' and not '}' and not ']' && !char.IsWhiteSpace(input[index])) index++;
            return index > start;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }

        private static bool IsHex(ReadOnlySpan<char> value) =>
            value.Length == 4 && value.ToString().All(Uri.IsHexDigit);
    }
}
