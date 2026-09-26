using System.Text;
using System.Text.Json;

namespace Jarvis.Application.Conversations;

/// <summary>Restores metadata order after storage in PostgreSQL jsonb.</summary>
public static class AgentSessionJson
{
    public static string PrepareForRead(string value)
    {
        using var document = JsonDocument.Parse(value);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) Write(document.RootElement, writer);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            // The Agent Framework's serializers require $type/$id before ordinary properties.
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name.StartsWith('$') ? 0 : 1))
            {
                writer.WritePropertyName(property.Name);
                Write(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) Write(item, writer);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }
}
