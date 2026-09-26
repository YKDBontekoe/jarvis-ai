using System.Text.Json;

namespace Jarvis.Application.Conversations;

/// <summary>Parses stored tool-call JSON into primitive argument values the agent can bind.</summary>
public static class ToolCallArguments
{
    public static Dictionary<string, object?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Tool call arguments must be a JSON object.");
        return ReadObject(document.RootElement);
    }

    private static Dictionary<string, object?> ReadObject(JsonElement element)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            arguments[property.Name] = ReadValue(property.Value);
        return arguments;
    }

    private static object? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(ReadValue).ToArray(),
        JsonValueKind.Object => ReadObject(element),
        _ => element.Clone()
    };
}
