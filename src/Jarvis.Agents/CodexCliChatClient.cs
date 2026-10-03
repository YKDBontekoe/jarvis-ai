using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Jarvis.Agents.Telemetry;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>
/// Bridges Microsoft.Extensions.AI to the authenticated Codex CLI app-server. Codex's MCP servers
/// are disabled and its own tools follow <see cref="CodexAccess"/>; Jarvis functions are returned as
/// structured calls and remain under Agent Framework's normal execution and approval pipeline.
/// </summary>
public sealed partial class CodexCliChatClient(CodexExecutable executable, string? model = null, string? visionModel = null,
    IReadOnlyDictionary<string, string>? modelClasses = null, bool enableWebSearch = true,
    int turnTimeoutSeconds = 300, CodexProcessLimiter? processLimiter = null, CodexAccess? access = null) : IChatClient
{
    private readonly CodexAccess _access = access ?? CodexAccess.Open;

    private static readonly ActivitySource ActivitySource = new("Jarvis.CodexChatClient");
    private static readonly Meter Meter = new("Jarvis.CodexChatClient");
    private static readonly Histogram<double> ModelCallDuration = Meter.CreateHistogram<double>(
        "jarvis.model.call.duration", "ms", "Duration of a Codex model completion.");
    private static readonly Counter<long> ModelCalls = Meter.CreateCounter<long>(
        "jarvis.model.calls", "{call}", "Codex model completion outcomes.");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>(
        "jarvis.model.tokens.input", "{token}", "Input tokens reported by Codex.");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>(
        "jarvis.model.tokens.output", "{token}", "Output tokens reported by Codex.");
    private static readonly Counter<long> CachedInputTokens = Meter.CreateCounter<long>(
        "jarvis.model.tokens.cached_input", "{token}", "Cached input tokens reported by Codex.");
    private static readonly Counter<long> ReasoningOutputTokens = Meter.CreateCounter<long>(
        "jarvis.model.tokens.reasoning_output", "{token}", "Reasoning output tokens reported by Codex.");
    private static readonly Counter<long> WebSearchActions = Meter.CreateCounter<long>(
        "jarvis.codex.web_search.actions", "{action}", "Codex native web-search actions completed.");
    private const int MaxPromptLength = 768_000;
    private const int MaxOutputLength = 64_000;
    private const int MaxImageDataUriLength = 12_000_000;
    private const int MaxTotalImageDataLength = 16_000_000;
    private const int MaxImageCount = 4;
    private static readonly TimeSpan ModelCatalogLifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Prompt text is not HTML; keep '+', quotes-in-text, and non-ASCII readable while
    // control characters and newlines stay escaped so a value cannot forge a role marker.
    private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly HashSet<string> ModelClassNames = new(StringComparer.OrdinalIgnoreCase)
        { "fast", "standard", "reasoning", "coding", "vision", "realtime" };
    private readonly Dictionary<string, string> _modelClasses = (modelClasses ?? new Dictionary<string, string>())
        .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
        .ToDictionary(item => item.Key.Trim(), item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase);
    private readonly CodexProcessLimiter _processSlots = processLimiter ?? new CodexProcessLimiter();
    private readonly TimeSpan _turnTimeout = TimeSpan.FromSeconds(turnTimeoutSeconds);
    private readonly SemaphoreSlim _modelCatalogLock = new(1, 1);
    private readonly object _modelCatalogSync = new();
    private AvailableModel[]? _modelCatalog;
    private string? _modelCatalogExecutable;
    private DateTimeOffset _modelCatalogExpiresAt;
    private static readonly JsonObject OutputSchema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "type": { "type": "string", "enum": ["text", "tool_call"] },
            "text": { "type": "string" },
            "name": { "type": "string" },
            "argumentsJson": { "type": "string" }
          },
          "required": ["type", "text", "name", "argumentsJson"],
          "additionalProperties": false
        }
        """)!.AsObject();

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var tools = options?.Tools?.OfType<AIFunction>().ToArray() ?? [];
        var prompt = BuildPrompt(messages, options, tools, enableWebSearch, _access.AllowShell);
        return await RunCodexAsync(prompt, options?.ModelId ?? model,
            GetReasoningEffort(options),
            tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal), null, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var tools = options?.Tools?.OfType<AIFunction>().ToArray() ?? [];
        var prompt = BuildPrompt(messages, options, tools, enableWebSearch, _access.AllowShell);
        var updates = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        var runTask = RunCodexAsync(prompt, options?.ModelId ?? model,
            GetReasoningEffort(options),
            tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal), updates.Writer, cancellationToken);
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken))
                yield return update;
        }
        finally
        {
            try { _ = await runTask; }
            catch when (cancellationToken.IsCancellationRequested) { }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    private static string? GetReasoningEffort(ChatOptions? options) =>
        options?.AdditionalProperties is not null &&
        options.AdditionalProperties.TryGetValue("reasoning_effort", out var effort)
            ? effort?.ToString()
            : null;

    public void Dispose()
    {
        if (processLimiter is null) _processSlots.Dispose();
        _modelCatalogLock.Dispose();
    }

    private string? ResolveModelSelector(string? selector)
    {
        if (selector is null) return null;
        if (_modelClasses.TryGetValue(selector, out var modelId)) return modelId;
        if (ModelClassNames.Contains(selector))
            throw new InvalidOperationException(
                $"Codex model class '{selector}' is not configured. Set Codex:ModelClasses:{selector} to a model available to the signed-in account.");
        return selector;
    }

    private async Task<ChatResponse> RunCodexAsync(PromptPayload prompt, string? requestedModel,
        string? reasoningEffort, IReadOnlySet<string> toolNames,
        ChannelWriter<ChatResponseUpdate>? updates, CancellationToken cancellationToken)
    {
        await _processSlots.WaitAsync(cancellationToken);
        var scratch = Path.Combine(Path.GetTempPath(), "jarvis-codex-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            var executablePath = executable.Resolve();
            using var process = new Process { StartInfo = CreateAppServerStart(executablePath, scratch, enableWebSearch, _access) };
            if (!process.Start()) throw new InvalidOperationException("Could not start the Codex CLI app-server.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_turnTimeout);
            var runToken = timeout.Token;
            using var killOnCancellation = runToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var writer = process.StandardInput;
            using var reader = process.StandardOutput;
            try
            {
                var rpc = new AppServerConnection(writer, reader, stderrTask, _access, scratch);
                await rpc.InitializeAsync(runToken);
                var modality = prompt.Images.Count > 0 ? "image" : "text";
                var hasVisionClass = _modelClasses.ContainsKey("vision");
                var selectedModel = prompt.Images.Count > 0
                    ? visionModel ?? (hasVisionClass ? "vision" : requestedModel)
                    : requestedModel;
                var modelCatalog = await GetModelCatalogAsync(rpc, executablePath, runToken);
                var resolvedModel = ResolveModel(modelCatalog, ResolveModelSelector(selectedModel), modality,
                    allowCompatibleFallback: prompt.Images.Count > 0 && visionModel is null && !hasVisionClass);
                var threadId = await rpc.StartThreadAsync(scratch, resolvedModel, runToken);
                var rawResponse = new StringBuilder();
                StructuredTextStreamDecoder textDecoder = new();
                string? agentMessageId = null;
                void ReportNativeTool(string callId, string phase) =>
                    updates?.TryWrite(NativeToolProgress.Create(callId, NativeToolProgress.WebSearch, phase));
                // Codex can answer one turn with several agent messages, for example the same structured
                // response twice or a short one before a hosted web search. Each message is a complete
                // response on its own, so only the last one is parsed.
                void OnDelta(string? itemId, string delta)
                {
                    if (itemId is not null && agentMessageId is not null && itemId != agentMessageId)
                    {
                        rawResponse.Clear();
                        textDecoder.StartNextMessage();
                    }
                    agentMessageId = itemId ?? agentMessageId;
                    if (rawResponse.Length + delta.Length > MaxOutputLength)
                        throw new InvalidOperationException("Codex CLI response exceeded the output size limit.");
                    rawResponse.Append(delta);
                    var textDelta = textDecoder.Append(delta);
                    if (!string.IsNullOrEmpty(textDelta))
                    {
                        updates?.TryWrite(new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(textDelta)],
                            ModelId = resolvedModel
                        });
                    }
                }

                using var modelActivity = ActivitySource.StartActivity("jarvis.model.completion");
                GenAiTelemetry.TagChat(modelActivity, "openai", resolvedModel);
                modelActivity?.SetTag("gen_ai.request.input_images", prompt.Images.Count);
                var modelCallStarted = Stopwatch.GetTimestamp();
                var modelOutcome = "failed";
                CodexTokenUsage? usage = null;
                var webSearches = 0;
                try
                {
                    var turn = await rpc.RunTurnAsync(threadId, prompt, resolvedModel, reasoningEffort, OnDelta,
                        runToken, ReportNativeTool);
                    usage = turn.Usage;
                    webSearches += turn.WebSearchActions;
                    modelOutcome = "completed";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    modelOutcome = "cancelled";
                    throw;
                }
                catch (OperationCanceledException)
                {
                    modelOutcome = "timeout";
                    throw new TimeoutException(
                        $"Codex CLI model turn exceeded {_turnTimeout.TotalSeconds:0} seconds.");
                }
                finally
                {
                    var elapsedMs = Stopwatch.GetElapsedTime(modelCallStarted).TotalMilliseconds;
                    var tags = new TagList { { "model", resolvedModel }, { "outcome", modelOutcome } };
                    ModelCallDuration.Record(elapsedMs, tags);
                    ModelCalls.Add(1, tags);
                    modelActivity?.SetTag("gen_ai.response.outcome", modelOutcome);
                    modelActivity?.SetTag("gen_ai.response.duration_ms", elapsedMs);
                }
                if (usage is { } tokenUsage)
                {
                    var tags = new TagList { { "model", resolvedModel } };
                    InputTokens.Add(tokenUsage.InputTokens, tags);
                    OutputTokens.Add(tokenUsage.OutputTokens, tags);
                    CachedInputTokens.Add(tokenUsage.CachedInputTokens, tags);
                    ReasoningOutputTokens.Add(tokenUsage.ReasoningOutputTokens, tags);
                    GenAiTelemetry.TagUsage(modelActivity, tokenUsage.InputTokens, tokenUsage.OutputTokens,
                        tokenUsage.CachedInputTokens, tokenUsage.ReasoningOutputTokens);
                }

                if (rawResponse.Length == 0)
                    throw new InvalidOperationException("Codex CLI completed without an assistant response.");

                using var document = ParseLastResponse(rawResponse.ToString());
                ChatMessage assistant;
                try
                {
                    assistant = ParseAssistantMessage(document.RootElement, toolNames);
                }
                catch (InvalidOperationException exception) when (RetryInstruction(exception) is not null)
                {
                    // The structured response schema stores function arguments as JSON text so it can
                    // represent each tool's own schema. If the model returns malformed JSON in that
                    // string, or names a function that does not exist, ask once for a corrected
                    // response instead of failing the whole turn.
                    rawResponse.Clear();
                    textDecoder = new StructuredTextStreamDecoder();
                    agentMessageId = null;
                    var retryPrompt = prompt with { Text = prompt.Text + "\n\n" + RetryInstruction(exception) };
                    var retryStarted = Stopwatch.GetTimestamp();
                    var retryOutcome = "failed";
                    try
                    {
                        var retry = await rpc.RunTurnAsync(threadId, retryPrompt, resolvedModel, reasoningEffort,
                            OnDelta, runToken, ReportNativeTool);
                        usage = AddUsage(usage, retry.Usage);
                        webSearches += retry.WebSearchActions;
                        if (retry.Usage is { } retryUsage)
                        {
                            var retryTags = new TagList { { "model", resolvedModel } };
                            InputTokens.Add(retryUsage.InputTokens, retryTags);
                            OutputTokens.Add(retryUsage.OutputTokens, retryTags);
                            CachedInputTokens.Add(retryUsage.CachedInputTokens, retryTags);
                            ReasoningOutputTokens.Add(retryUsage.ReasoningOutputTokens, retryTags);
                        }
                        retryOutcome = "completed";
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException(
                            $"Codex CLI model turn exceeded {_turnTimeout.TotalSeconds:0} seconds.");
                    }
                    finally
                    {
                        var elapsedMs = Stopwatch.GetElapsedTime(retryStarted).TotalMilliseconds;
                        var tags = new TagList { { "model", resolvedModel }, { "outcome", retryOutcome } };
                        ModelCallDuration.Record(elapsedMs, tags);
                        ModelCalls.Add(1, tags);
                    }
                    if (rawResponse.Length == 0)
                        throw new InvalidOperationException("Codex CLI retry completed without an assistant response.");
                    using var retryDocument = ParseLastResponse(rawResponse.ToString());
                    assistant = ParseAssistantMessage(retryDocument.RootElement, toolNames);
                }
                if (assistant.Contents.Count == 1 && assistant.Contents[0] is FunctionCallContent functionCall)
                {
                    updates?.TryWrite(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [functionCall],
                        ModelId = resolvedModel
                    });
                }
                else if (updates is not null)
                {
                    var remaining = textDecoder.Complete();
                    if (!string.IsNullOrEmpty(remaining))
                        updates.TryWrite(new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(remaining)],
                            ModelId = resolvedModel
                        });
                }

                var reportedUsage = ToUsageDetails(usage, webSearches);
                if (reportedUsage is not null)
                    updates?.TryWrite(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        ModelId = resolvedModel,
                        Contents = [new UsageContent(reportedUsage)]
                    });
                writer.Close();
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await process.WaitForExitAsync(shutdown.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }
                return new ChatResponse(assistant) { ModelId = resolvedModel, Usage = reportedUsage };
            }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                try { await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { }
            }
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            _processSlots.Release();
            updates?.TryComplete();
        }
    }

    internal static ProcessStartInfo CreateAppServerStart(string executable, string workingDirectory,
        bool enableWebSearch = true, CodexAccess? access = null)
    {
        access ??= CodexAccess.Open;
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // The model process only needs Codex OAuth and the runtime path. Do not pass database,
        // object-storage, account tokens, or application secrets to the child process.
        var inheritedPath = Environment.GetEnvironmentVariable("PATH");
        var home = Environment.GetEnvironmentVariable("HOME");
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(inheritedPath)) start.Environment["PATH"] = inheritedPath;
        if (!string.IsNullOrWhiteSpace(home)) start.Environment["HOME"] = home;
        if (!string.IsNullOrWhiteSpace(codexHome)) start.Environment["CODEX_HOME"] = codexHome;
        var passedThrough = new[] { "TMPDIR", "SSL_CERT_FILE", "SSL_CERT_DIR", "LANG", "LC_ALL" };
        if (access.AllowNetwork) passedThrough = [.. passedThrough, .. CodexAccess.NetworkEnvironment];
        foreach (var key in passedThrough)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[key] = value;
        }

        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--stdio");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("mcp_servers={}");
        foreach (var feature in access.DisabledFeatures(enableWebSearch))
        {
            start.ArgumentList.Add("--disable");
            start.ArgumentList.Add(feature);
        }
        // Chat turns use Codex's hosted web_search tool in live mode. The CLI's standalone search feature is
        // still under development: it replaces the hosted tool with a client-side web.run call to a separate
        // alpha endpoint, and when that call is missing or fails the model can only report that search is
        // unavailable.
        start.ArgumentList.Add("--disable");
        start.ArgumentList.Add("standalone_web_search");
        foreach (var overridePair in enableWebSearch ? LiveWebSearchConfigOverrides : DisabledWebSearchConfigOverrides)
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(overridePair);
        }
        return start;
    }

    internal static readonly string[] LiveWebSearchConfigOverrides = ["web_search=\"live\""];

    internal static readonly string[] DisabledWebSearchConfigOverrides = ["web_search=\"disabled\""];

    private async Task<AvailableModel[]> GetModelCatalogAsync(AppServerConnection rpc, string executablePath,
        CancellationToken cancellationToken)
    {
        lock (_modelCatalogSync)
        {
            if (_modelCatalog is not null && _modelCatalogExecutable == executablePath &&
                _modelCatalogExpiresAt > DateTimeOffset.UtcNow)
                return _modelCatalog;
        }

        await _modelCatalogLock.WaitAsync(cancellationToken);
        try
        {
            lock (_modelCatalogSync)
            {
                if (_modelCatalog is not null && _modelCatalogExecutable == executablePath &&
                    _modelCatalogExpiresAt > DateTimeOffset.UtcNow)
                    return _modelCatalog;
            }

            var catalog = await rpc.ListModelsAsync(cancellationToken);
            lock (_modelCatalogSync)
            {
                _modelCatalog = catalog;
                _modelCatalogExecutable = executablePath;
                _modelCatalogExpiresAt = DateTimeOffset.UtcNow + ModelCatalogLifetime;
                return _modelCatalog;
            }
        }
        finally
        {
            _modelCatalogLock.Release();
        }
    }

    private static string ResolveModel(IReadOnlyList<AvailableModel> models, string? requestedModel,
        string requiredModality, bool allowCompatibleFallback)
    {
        if (requestedModel is not null)
        {
            var requested = models.FirstOrDefault(candidate =>
                string.Equals(candidate.Model, requestedModel, StringComparison.Ordinal) ||
                string.Equals(candidate.Id, requestedModel, StringComparison.Ordinal));
            if (requested is not null && requested.InputModalities.Contains(requiredModality))
                return requested.Model;
            if (!allowCompatibleFallback)
                throw new InvalidOperationException(requested is null
                    ? $"Configured Codex model '{requestedModel}' is not available to the signed-in account."
                    : $"Configured Codex model '{requestedModel}' does not support {requiredModality} input.");
        }

        var defaultModel = models.FirstOrDefault(candidate => candidate.IsDefault &&
            candidate.InputModalities.Contains(requiredModality));
        if (defaultModel is not null) return defaultModel.Model;
        var compatibleModel = models.FirstOrDefault(candidate => candidate.InputModalities.Contains(requiredModality));
        if (compatibleModel is not null) return compatibleModel.Model;

        throw new InvalidOperationException(
            $"The signed-in Codex account has no available model supporting {requiredModality} input.");
    }

    private readonly record struct CodexTokenUsage(long InputTokens, long OutputTokens,
        long CachedInputTokens, long ReasoningOutputTokens);

    private readonly record struct CodexTurnMetrics(CodexTokenUsage? Usage, int WebSearchActions);

    private static CodexTokenUsage? AddUsage(CodexTokenUsage? left, CodexTokenUsage? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return new CodexTokenUsage(left.Value.InputTokens + right.Value.InputTokens,
            left.Value.OutputTokens + right.Value.OutputTokens,
            left.Value.CachedInputTokens + right.Value.CachedInputTokens,
            left.Value.ReasoningOutputTokens + right.Value.ReasoningOutputTokens);
    }

    private static UsageDetails? ToUsageDetails(CodexTokenUsage? usage, int webSearches)
    {
        if (usage is null && webSearches <= 0) return null;
        var details = new UsageDetails
        {
            InputTokenCount = usage?.InputTokens ?? 0,
            OutputTokenCount = usage?.OutputTokens ?? 0,
            TotalTokenCount = (usage?.InputTokens ?? 0) + (usage?.OutputTokens ?? 0),
            CachedInputTokenCount = usage?.CachedInputTokens ?? 0,
            ReasoningTokenCount = usage?.ReasoningOutputTokens ?? 0
        };
        if (webSearches > 0)
        {
            details.AdditionalCounts ??= new AdditionalPropertiesDictionary<long>();
            details.AdditionalCounts["webSearchActions"] = webSearches;
        }
        return details;
    }

    private sealed record AvailableModel(string Model, string? Id, IReadOnlySet<string> InputModalities, bool IsDefault);

    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
