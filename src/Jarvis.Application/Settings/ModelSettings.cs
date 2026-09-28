using System.Text.RegularExpressions;

namespace Jarvis.Application.Settings;

/// <summary>
/// Owner-selected language model routing. Codex uses the host's signed-in ChatGPT OAuth session; OpenRouter uses
/// the owner's own API key stored in the encrypted credential store.
/// </summary>
public sealed partial record ModelSettings(
    string Provider,
    string? ChatModel = null,
    string? FastModel = null,
    string? ReasoningModel = null,
    string? EmbeddingModel = null)
{
    public const string Codex = "codex";
    public const string OpenRouter = "openrouter";
    public const string OpenRouterKeySecret = "api_key";

    public static ModelSettings Default { get; } = new(Codex);

    public static IReadOnlyList<string> Providers { get; } = [Codex, OpenRouter];

    public bool UsesOpenRouter => Provider == OpenRouter;

    /// <summary>OpenRouter is used for semantic memory when an embedding model is configured.</summary>
    public bool UsesOpenRouterEmbeddings => !string.IsNullOrEmpty(EmbeddingModel);

    /// <summary>Returns a normalized copy or throws <see cref="ArgumentException"/> for invalid input.</summary>
    public ModelSettings Normalize()
    {
        var provider = Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Providers.Contains(provider))
            throw new ArgumentException("Choose codex or openrouter as the model provider.", nameof(Provider));
        var chat = NormalizeModel(ChatModel, nameof(ChatModel));
        var fast = NormalizeModel(FastModel, nameof(FastModel));
        var reasoning = NormalizeModel(ReasoningModel, nameof(ReasoningModel));
        var embedding = NormalizeModel(EmbeddingModel, nameof(EmbeddingModel));
        if (provider == OpenRouter && chat is null)
            throw new ArgumentException("Choose an OpenRouter chat model, such as anthropic/claude-sonnet-4.5.",
                nameof(ChatModel));
        return new ModelSettings(provider, chat, fast, reasoning, embedding);
    }

    private static string? NormalizeModel(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (!ModelIdPattern().IsMatch(trimmed))
            throw new ArgumentException("Model ids may contain letters, digits, and . _ : / @ - only (up to 200 characters).",
                field);
        return trimmed;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:/@\-]{1,200}$")]
    private static partial Regex ModelIdPattern();
}
