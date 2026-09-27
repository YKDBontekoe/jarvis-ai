using System.Text.Json;

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

public interface IUiSurfaceRepository
{
    Task<UiSurfaceRecord> CreateAsync(Guid ownerId, Guid conversationId, string kind, string title, string schemaJson,
        CancellationToken cancellationToken);
    Task<UiSurfaceRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<UiSurfaceRecord>> ListForConversationAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken);
    Task<UiSurfaceRecord?> CompleteAsync(Guid ownerId, Guid id, string actionId, string valuesJson,
        CancellationToken cancellationToken);
    Task ReopenAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
}

/// <summary>A2UI-inspired generative UI: a small native widget tree Jarvis can render in the app.</summary>
public static class UiSurfaceSchema
{
    public const int MaxTitle = 80;
    public const int MaxBody = 2_000;
    public const int MaxItems = 20;
    public const int MaxFields = 8;
    public const int MaxActions = 6;
    private static readonly HashSet<string> FieldTypes = ["text", "number", "toggle", "choice"];
    private static readonly HashSet<string> ActionStyles = ["primary", "secondary", "danger"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Normalize(string? kind, string? title, string? body, JsonElement? items, JsonElement? fields,
        JsonElement? actions)
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

        var document = new Dictionary<string, object?>
        {
            ["kind"] = normalizedKind,
            ["title"] = normalizedTitle,
            ["body"] = string.IsNullOrEmpty(normalizedBody) ? null : normalizedBody,
            ["items"] = ReadItems(items),
            ["fields"] = ReadFields(fields),
            ["actions"] = ReadActions(actions)
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

    private static object[] ReadFields(JsonElement? fields)
    {
        if (fields is not { ValueKind: JsonValueKind.Array } array) return [];
        if (array.GetArrayLength() > MaxFields) throw new ArgumentException($"Use at most {MaxFields} fields.");
        var result = new List<object>();
        foreach (var field in array.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(field, "id", 40) ?? throw new ArgumentException("Each field needs an id.");
            var type = (ReadString(field, "type", 20) ?? "text").ToLowerInvariant();
            if (!FieldTypes.Contains(type)) throw new ArgumentException($"Unsupported field type '{type}'.");
            result.Add(new
            {
                id,
                label = ReadString(field, "label", 80) ?? id,
                type,
                placeholder = ReadString(field, "placeholder", 80),
                options = ReadStringList(field, "options", 12)
            });
        }
        return [.. result];
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
            result.Add(new
            {
                id,
                label = ReadString(action, "label", 40) ?? id,
                style
            });
        }
        return [.. result];
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
