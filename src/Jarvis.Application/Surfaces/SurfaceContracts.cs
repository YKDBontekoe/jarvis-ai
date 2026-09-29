using System.Text.Json;
using Jarvis.Application.Integrations;

namespace Jarvis.Application.Surfaces;

public static class UiSurfaceKinds
{
    public const string Card = "card";
    public const string Form = "form";
    public const string Choice = "choice";
    public const string Status = "status";
    public const string List = "list";

    public static readonly HashSet<string> All = [Card, Form, Choice, Status, List];
}

public sealed record UiSurfaceRecord(
    Guid Id,
    Guid OwnerId,
    Guid ConversationId,
    string Kind,
    string Title,
    string SchemaJson,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UiSurfaceAction(string ActionId, IReadOnlyDictionary<string, string> Values);

/// <summary>A newly rendered card, plus any older open cards in that conversation that were closed.</summary>
public sealed record UiSurfaceCreateResult(UiSurfaceRecord Surface, IReadOnlyList<UiSurfaceRecord> Replaced);

public interface IUiSurfaceRepository
{
    Task<UiSurfaceCreateResult> CreateAsync(Guid ownerId, Guid conversationId, string kind, string title,
        string schemaJson, CancellationToken cancellationToken);
    Task<UiSurfaceRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<UiSurfaceRecord>> ListForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task<UiSurfaceRecord?> CompleteAsync(Guid ownerId, Guid id, string actionId, string valuesJson,
        CancellationToken cancellationToken);

    /// <summary>Reopens a completed card and closes any other open card in the same conversation.</summary>
    Task<IReadOnlyList<UiSurfaceRecord>> ReopenAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
}

/// <summary>A2UI-inspired generative UI: a small native widget tree Jarvis can render in the app.</summary>
public static class UiSurfaceSchema
{
    public const int MaxTitle = 80;
    public const int MaxBody = 2_000;
    public const int MaxItems = 20;
    public const int MaxFields = 8;
    public const int MaxActions = 6;
    public const int MaxActionUrl = 2_000;
    private static readonly HashSet<string> FieldTypes = ["text", "number", "toggle", "choice", "secret"];
    private static readonly HashSet<string> ActionStyles = ["primary", "secondary", "danger"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Normalize(string? kind, string? title, string? body, JsonElement? items, JsonElement? fields,
        JsonElement? actions, string? credentialProvider = null)
    {
        var normalizedKind = (kind ?? UiSurfaceKinds.Card).Trim().ToLowerInvariant();
        if (!UiSurfaceKinds.All.Contains(normalizedKind))
            throw new ArgumentException("Use card, form, choice, status, or list as the UI kind.");
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length is 0 or > MaxTitle)
            throw new ArgumentException("Give the UI a title of 1 to 80 characters.");
        var normalizedBody = (body ?? string.Empty).Trim();
        if (normalizedBody.Length > MaxBody)
            throw new ArgumentException("Keep the UI body under 2,000 characters.");
        var readFields = ReadFields(fields);
        var provider = NormalizeCredentialProvider(credentialProvider, readFields.Any(field => field.Type == "secret"));

        var document = new Dictionary<string, object?>
        {
            ["kind"] = normalizedKind,
            ["title"] = normalizedTitle,
            ["body"] = string.IsNullOrEmpty(normalizedBody) ? null : normalizedBody,
            ["items"] = ReadItems(items),
            ["fields"] = readFields.Select(field => field.ToDocument()).ToArray(),
            ["actions"] = ReadActions(actions),
            ["credentialProvider"] = provider
        };
        return JsonSerializer.Serialize(document, Json);
    }

    public static bool TryParse(string schemaJson, out JsonElement schema)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            schema = document.RootElement.Clone();
            return schema.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            schema = default;
            return false;
        }
    }

    private static object[] ReadItems(JsonElement? items)
    {
        if (items is not { ValueKind: JsonValueKind.Array } array) return [];
        if (array.GetArrayLength() > MaxItems) throw new ArgumentException($"List at most {MaxItems} items.");
        var result = new List<object>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(item, "id", 40) ?? $"item-{result.Count + 1}";
            var title = ReadString(item, "title", 120) ?? id;
            result.Add(new
            {
                id,
                title,
                subtitle = ReadString(item, "subtitle", 200),
                detail = ReadString(item, "detail", 500)
            });
        }
        return [.. result];
    }

    private static List<UiSurfaceField> ReadFields(JsonElement? fields)
    {
        if (fields is not { ValueKind: JsonValueKind.Array } array) return [];
        if (array.GetArrayLength() > MaxFields) throw new ArgumentException($"Use at most {MaxFields} fields.");
        var result = new List<UiSurfaceField>();
        foreach (var field in array.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(field, "id", 40) ?? throw new ArgumentException("Each field needs an id.");
            var type = (ReadString(field, "type", 20) ?? "text").ToLowerInvariant();
            if (!FieldTypes.Contains(type)) throw new ArgumentException($"Unsupported field type '{type}'.");
            result.Add(new UiSurfaceField(
                id,
                ReadString(field, "label", 80) ?? id,
                type,
                ReadString(field, "placeholder", 80),
                ReadStringList(field, "options", 12),
                type == "secret"
                    ? ReadString(field, "secretName", 64) ?? IntegrationCredentialProviders.UserMcpTokenSecret
                    : null));
        }
        return result;
    }

    private static object[] ReadActions(JsonElement? actions)
    {
        if (actions is not { ValueKind: JsonValueKind.Array } array) return [];
        if (array.GetArrayLength() > MaxActions) throw new ArgumentException($"Use at most {MaxActions} actions.");
        var result = new List<object>();
        foreach (var action in array.EnumerateArray())
        {
            if (action.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(action, "id", 40) ?? throw new ArgumentException("Each action needs an id.");
            var style = (ReadString(action, "style", 20) ?? "secondary").ToLowerInvariant();
            if (!ActionStyles.Contains(style)) style = "secondary";
            var url = ReadString(action, "url", MaxActionUrl);
            if (url is not null && McpAuthorization.NormalizePublicHttpsUrl(url) is null)
                throw new ArgumentException("Action urls must be public HTTPS.");
            result.Add(new
            {
                id,
                label = ReadString(action, "label", 40) ?? id,
                style,
                url = url is null ? null : McpAuthorization.NormalizePublicHttpsUrl(url)
            });
        }
        return [.. result];
    }

    private static string? NormalizeCredentialProvider(string? credentialProvider, bool hasSecret)
    {
        var provider = credentialProvider?.Trim();
        if (string.IsNullOrEmpty(provider))
        {
            if (hasSecret)
                throw new ArgumentException(
                    "Secret fields need a credentialProvider so Jarvis can store the token without putting it in the conversation.");
            return null;
        }
        if (!IntegrationCredentialProviders.AllowsChatSecret(provider))
            throw new ArgumentException("That credential provider cannot be collected from a chat card.");
        return provider;
    }

    private readonly record struct UiSurfaceField(string Id, string Label, string Type, string? Placeholder,
        string[] Options, string? SecretName)
    {
        public object ToDocument() => new
        {
            id = Id,
            label = Label,
            type = Type,
            placeholder = Placeholder,
            options = Options,
            secretName = SecretName
        };
    }

    private static string? ReadString(JsonElement element, string name, int max)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= max ? text : text[..max];
    }

    private static string[] ReadStringList(JsonElement element, string name, int max)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
            .Where(item => !string.IsNullOrEmpty(item))
            .Cast<string>()
            .Take(max)
            .ToArray();
    }
}

/// <summary>Turns a completed generative UI card into a chat turn, without leaking secret field values.</summary>
public static class UiSurfaceAnswers
{
    public const int MaxFieldValues = 12;
    public const int MaxValueLength = 500;
    public const int MaxSecretValueLength = 8_192;
    public const string StoredMarker = "(stored securely)";

    public static bool IsSecretField(JsonElement schema, string fieldId)
    {
        foreach (var field in Fields(schema))
        {
            if (!field.TryGetProperty("id", out var id) || id.GetString() != fieldId) continue;
            return field.TryGetProperty("type", out var type) &&
                   type.ValueKind == JsonValueKind.String &&
                   type.GetString() == "secret";
        }
        return false;
    }

    public static int MaxLengthFor(JsonElement schema, string fieldId) =>
        IsSecretField(schema, fieldId) ? MaxSecretValueLength : MaxValueLength;

    public static bool IsAllowedValue(JsonElement schema, string key, string value) =>
        key.Length is > 0 and <= 40 && value.Length <= MaxLengthFor(schema, key);

    public static string? CredentialProvider(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object &&
        schema.TryGetProperty("credentialProvider", out var value) && value.ValueKind == JsonValueKind.String
            ? EmptyToNull(value.GetString())
            : null;

    public static string SecretName(JsonElement schema, string fieldId)
    {
        foreach (var field in Fields(schema))
        {
            if (!field.TryGetProperty("id", out var id) || id.GetString() != fieldId) continue;
            if (field.TryGetProperty("secretName", out var name) && name.ValueKind == JsonValueKind.String)
                return EmptyToNull(name.GetString()) ?? IntegrationCredentialProviders.UserMcpTokenSecret;
        }
        return IntegrationCredentialProviders.UserMcpTokenSecret;
    }

    public static IReadOnlyList<(string Provider, string SecretName, string Value)> SecretsToStore(JsonElement schema,
        IReadOnlyDictionary<string, string> values)
    {
        var provider = CredentialProvider(schema);
        if (provider is null) return [];
        var saved = new List<(string, string, string)>();
        foreach (var pair in values)
        {
            if (!IsSecretField(schema, pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
            saved.Add((provider, SecretName(schema, pair.Key), pair.Value));
        }
        return saved;
    }

    public static Dictionary<string, string> Redact(JsonElement schema, IReadOnlyDictionary<string, string> values)
    {
        var redacted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
            redacted[pair.Key] = IsSecretField(schema, pair.Key) ? StoredMarker : pair.Value;
        return redacted;
    }

    public static string Describe(string title, string actionId, IReadOnlyDictionary<string, string> values,
        JsonElement schema)
    {
        var stored = SecretsToStore(schema, values);
        if (values.TryGetValue("label", out var label) && !string.IsNullOrWhiteSpace(label) && stored.Count == 0)
            return $"I picked \"{label.Trim()}\".";
        var parts = new List<string>();
        if (stored.Count > 0)
            parts.Add($"I saved a credential for '{stored[0].Provider}' in this chat. It is stored securely and is not included here.");
        var answers = values
            .Where(pair => pair.Key is not "choice" and not "label" && !IsSecretField(schema, pair.Key) &&
                           !string.IsNullOrWhiteSpace(pair.Value) && pair.Value != StoredMarker)
            .Select(pair => $"{pair.Key}: {pair.Value.Trim()}")
            .ToArray();
        if (answers.Length > 0)
            parts.Add($"My answers for \"{title}\": {string.Join("; ", answers)}.");
        if (parts.Count > 0) return string.Join(" ", parts);
        return $"I chose '{actionId}' on the {title} card.";
    }

    private static IEnumerable<JsonElement> Fields(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("fields", out var fields) ||
            fields.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind == JsonValueKind.Object) yield return field;
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
