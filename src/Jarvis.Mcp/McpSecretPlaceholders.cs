using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jarvis.Application.Integrations;

namespace Jarvis.Mcp;

/// <summary>
/// Lets the agent name a stored secret in tool arguments without ever seeing its value. The model writes
/// <c>{{secret:password}}</c>; Jarvis swaps in the value from the server's own credential store just before the
/// call, so the approval card and transcript only show the placeholder.
/// </summary>
public static partial class McpSecretPlaceholders
{
    /// <summary>OAuth bookkeeping and the stored server definition are never handed to a tool as an argument.</summary>
    private static bool IsInternal(string name) =>
        name == IntegrationCredentialProviders.UserMcpConfigSecret ||
        name == IntegrationCredentialProviders.UserMcpStateSecret ||
        name.StartsWith("oauth_", StringComparison.Ordinal);

    public static bool Contains(string? argumentsJson) =>
        argumentsJson is not null && Placeholder().IsMatch(argumentsJson);

    /// <summary>The secret names an arguments object refers to, in order of first use.</summary>
    public static IReadOnlyList<string> Names(IReadOnlyDictionary<string, object?> arguments)
    {
        var names = new List<string>();
        foreach (var value in arguments.Values) Collect(value, names);
        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Replaces every placeholder in string values. <paramref name="missing"/> lists names the server has no value
    /// for; when it is not empty the result must not be sent.
    /// </summary>
    public static Dictionary<string, object?> Resolve(IReadOnlyDictionary<string, object?> arguments,
        IReadOnlyDictionary<string, string>? secrets, out IReadOnlyList<string> missing)
    {
        var absent = new List<string>();
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments) result[key] = Rewrite(value, secrets, absent);
        missing = absent.Distinct(StringComparer.Ordinal).ToArray();
        return result;
    }

    private static void Collect(object? value, List<string> names)
    {
        switch (value)
        {
            case string text:
                names.AddRange(Placeholder().Matches(text).Select(match => match.Groups[1].Value));
                break;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                Collect(element.GetString(), names);
                break;
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, names);
                break;
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                foreach (var item in element.EnumerateArray()) Collect(item, names);
                break;
        }
    }

    private static object? Rewrite(object? value, IReadOnlyDictionary<string, string>? secrets, List<string> absent)
    {
        switch (value)
        {
            case string text:
                return Substitute(text, secrets, absent);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return Substitute(element.GetString() ?? string.Empty, secrets, absent);
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                var node = new JsonObject();
                foreach (var property in element.EnumerateObject())
                    node[property.Name] = ToNode(Rewrite(property.Value, secrets, absent));
                return node;
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray()) array.Add(ToNode(Rewrite(item, secrets, absent)));
                return array;
            default:
                return value;
        }
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        string text => JsonValue.Create(text),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        _ => JsonSerializer.SerializeToNode(value)
    };

    private static string Substitute(string text, IReadOnlyDictionary<string, string>? secrets, List<string> absent) =>
        Placeholder().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            if (!IsInternal(name) && secrets is not null && secrets.TryGetValue(name, out var secret)) return secret;
            absent.Add(name);
            return match.Value;
        });

    [GeneratedRegex(@"\{\{secret:([a-z][a-z0-9_]{0,47})\}\}")]
    private static partial Regex Placeholder();
}
