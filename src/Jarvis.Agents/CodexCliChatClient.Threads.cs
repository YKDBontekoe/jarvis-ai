using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>
/// Keeps one Codex thread per conversation on the process that holds it. A model call then sends only what the
/// thread has not seen yet (a tool result, or the user's new message and that turn's context blocks) instead of the
/// whole prompt on a fresh thread. Codex ties prompt-cache routing and its Responses connection to the thread, so a
/// continuing thread is what lets the provider reuse the conversation it already processed.
///
/// The thread is only a cache. Whenever what it holds is not exactly the start of the current conversation (a reply
/// regenerated, history compacted, the instructions, tools or model changed, its process busy, gone or replaced), the
/// call falls back to the full prompt on a new thread, exactly as before.
/// </summary>
public sealed partial class CodexCliChatClient
{
    /// <summary>Set on calls that belong to one conversation; its value names the conversation.</summary>
    internal const string ConversationProperty = "jarvis_conversation";

    /// <summary>
    /// Characters sent into one thread before it is replaced. Every turn adds that turn's context blocks to the thread,
    /// so a long-lived thread is renewed from the current conversation before the copies pile up.
    /// </summary>
    internal const int MaxThreadChars = 400_000;
    private const int MaxConversationThreads = 256;
    private readonly Dictionary<string, ConversationThread> _threads = new(StringComparer.Ordinal);

    /// <summary>Marks every call through <paramref name="client"/> as part of one conversation.</summary>
    public static IChatClient ForConversation(IChatClient client, Guid conversationId) =>
        new ConversationChatClient(client, conversationId.ToString("N"));

    private sealed class ConversationChatClient(IChatClient inner, string conversationKey) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(messages, Mark(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(messages, Mark(options), cancellationToken);

        protected override void Dispose(bool disposing) { }

        private ChatOptions Mark(ChatOptions? options)
        {
            options = options?.Clone() ?? new ChatOptions();
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[ConversationProperty] = conversationKey;
            return options;
        }
    }

    /// <summary>What a conversation's Codex thread already holds.</summary>
    private sealed class ConversationThread(AppServerProcess process, string threadId, string model, string setup,
        string[] durable, string[] context, int sentChars)
    {
        public AppServerProcess Process { get; } = process;
        public string ThreadId { get; } = threadId;
        public string Model { get; } = model;
        public string Setup { get; } = setup;
        public string[] Durable { get; } = durable;
        public string[] Context { get; } = context;
        public int SentChars { get; } = sentChars;
    }

    /// <summary>
    /// One model call: the full prompt (always ready as the fallback) and, for a conversation, fingerprints of its
    /// stored messages and of this turn's context blocks.
    /// </summary>
    internal sealed record CodexRequest(PromptPayload Full, string? ConversationKey, string Setup,
        IReadOnlyList<ChatMessage> Messages, bool[] IsContext, string[] Durable, string[] Context)
    {
        public static CodexRequest Single(PromptPayload full) => new(full, null, "", [], [], [], []);
    }

    /// <summary>A thread this call continues, and the part of the conversation it has not seen.</summary>
    private sealed record Continuation(ConversationThread Thread, PromptPayload Prompt);

    private CodexRequest PlanRequest(IEnumerable<ChatMessage> messages, ChatOptions? options,
        IReadOnlyList<AIFunction> tools)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var full = BuildPrompt(list, options, tools, enableWebSearch, _access.AllowShell);
        if (!conversationThreads || ConversationKey(options) is not { } key) return CodexRequest.Single(full);

        var isContext = list.Select(IsContextBlock).ToArray();
        var durable = list.Where((_, index) => !isContext[index]).Select(Fingerprint).ToArray();
        var context = list.Where((_, index) => isContext[index]).Select(Fingerprint).ToArray();
        var setup = Hash(BuildSetup(options, tools, enableWebSearch, _access.AllowShell));
        return new CodexRequest(full, key, setup, list, isContext, durable, context);
    }

    private static string? ConversationKey(ChatOptions? options) =>
        options?.AdditionalProperties is not null &&
        options.AdditionalProperties.TryGetValue(ConversationProperty, out var value) &&
        value is string { Length: > 0 } key
            ? key
            : null;

    /// <summary>Context blocks are rebuilt every turn and never stored, so they are not part of the conversation.</summary>
    private static bool IsContextBlock(ChatMessage message) =>
        message.GetAgentRequestMessageSourceType() == AgentRequestMessageSourceType.AIContextProvider;

    /// <summary>
    /// Claims the conversation's thread when it can continue: its process is idle and alive, nothing before the
    /// conversation changed, and everything it holds is the start of the current conversation. The claimed process
    /// leaves the idle pool, so nothing else uses it during this call.
    /// </summary>
    private Continuation? TryContinue(CodexRequest request, string executablePath)
    {
        if (request.ConversationKey is not { } key) return null;
        lock (_poolSync)
        {
            if (!_threads.Remove(key, out var thread)) return null;
            if (thread.Setup != request.Setup || thread.SentChars >= MaxThreadChars ||
                thread.Durable.Length > request.Durable.Length ||
                !thread.Durable.AsSpan().SequenceEqual(request.Durable.AsSpan(0, thread.Durable.Length)))
                return null;
            var contextChanged = !thread.Context.AsSpan().SequenceEqual(request.Context);
            var unseen = request.Durable.Length - thread.Durable.Length;
            // Nothing new to say: a repeat of the same call is safest as a fresh one.
            if (unseen == 0 && !contextChanged) return null;
            if (!thread.Process.CanServe(executablePath, DateTimeOffset.UtcNow, IdleProcessLifetime) ||
                !_idle.Remove(thread.Process))
                return null;

            var durableIndex = -1;
            var include = new bool[request.Messages.Count];
            for (var index = 0; index < include.Length; index++)
            {
                if (request.IsContext[index]) include[index] = contextChanged;
                else include[index] = ++durableIndex >= thread.Durable.Length;
            }
            var prompt = BuildContinuation(request.Messages, index => include[index], contextChanged);
            return new Continuation(thread, prompt);
        }
    }

    /// <summary>Records what the thread holds after this call: the conversation so far plus the reply just given.</summary>
    private void RememberThread(CodexRequest request, AppServerProcess process, string threadId, string model,
        ChatMessage reply, int sentChars)
    {
        if (request.ConversationKey is not { } key) return;
        var thread = new ConversationThread(process, threadId, model, request.Setup,
            [.. request.Durable, Fingerprint(reply)], request.Context, sentChars);
        lock (_poolSync)
        {
            _threads[key] = thread;
            if (_threads.Count <= MaxConversationThreads) return;
            // Forget threads whose process is gone first, then any other one.
            var stale = _threads.FirstOrDefault(pair => !pair.Value.Process.IsAlive).Key ??
                        _threads.Keys.First(candidate => candidate != key);
            _threads.Remove(stale);
        }
    }

    private void ForgetThread(string? key)
    {
        if (key is null) return;
        lock (_poolSync) _threads.Remove(key);
    }

    /// <summary>
    /// Identifies a message by what the model sees of it. Text parts are joined, because a streamed reply is stored
    /// as many small text parts while the reply Jarvis returned holds them as one.
    /// </summary>
    internal static string Fingerprint(ChatMessage message)
    {
        var text = new StringBuilder();
        text.Append(message.Role.Value).Append('\n');
        text.Append(string.Concat(message.Contents.OfType<TextContent>().Select(part => part.Text)));
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    text.Append("\ncall:").Append(call.Name).Append(' ').Append(Canonical(call.Arguments));
                    break;
                case FunctionResultContent result:
                    text.Append("\nresult:").Append(result.CallId).Append(' ').Append(Canonical(result.Result));
                    break;
                case DataContent data:
                    text.Append("\ndata:").Append(data.MediaType).Append(' ').Append(Hash(data.Uri));
                    break;
            }
        }
        return Hash(text.ToString());
    }

    /// <summary>
    /// The same value written the same way, whether it is the object a tool returned or the JSON it became when the
    /// conversation was saved and loaded, which may order properties differently and keep the stored escaping (such as
    /// <c>\u002B</c> for <c>+</c>).
    /// </summary>
    internal static string Canonical(object? value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, PromptJsonOptions));
        var text = new StringBuilder();
        Write(document.RootElement);
        return text.ToString();

        void Write(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    text.Append('{');
                    // Property order is not kept when a conversation is saved and loaded.
                    foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    {
                        text.Append(JsonSerializer.Serialize(property.Name, PromptJsonOptions)).Append(':');
                        Write(property.Value);
                        text.Append(',');
                    }
                    text.Append('}');
                    break;
                case JsonValueKind.Array:
                    text.Append('[');
                    foreach (var item in element.EnumerateArray())
                    {
                        Write(item);
                        text.Append(',');
                    }
                    text.Append(']');
                    break;
                case JsonValueKind.String:
                    text.Append(JsonSerializer.Serialize(element.GetString(), PromptJsonOptions));
                    break;
                default:
                    text.Append(element.GetRawText());
                    break;
            }
        }
    }

    /// <summary>True when a conversation's thread lives in <paramref name="process"/>. Call under the pool lock.</summary>
    private bool HoldsThreadLocked(AppServerProcess process) =>
        _threads.Values.Any(thread => ReferenceEquals(thread.Process, process));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
