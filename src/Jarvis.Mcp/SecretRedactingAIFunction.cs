using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Application;
using Microsoft.Extensions.AI;

namespace Jarvis.Mcp;

internal sealed class SecretRedactingAIFunction : DelegatingAIFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string[] _secrets;

    public SecretRedactingAIFunction(AIFunction function, IEnumerable<string> secrets) : base(function) =>
        _secrets = secrets.Where(secret => !string.IsNullOrEmpty(secret))
            .Distinct(StringComparer.Ordinal).OrderByDescending(secret => secret.Length).ToArray();

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            if (result is null || _secrets.Length == 0) return result;
            if (result is string text) return RedactText(text);

            var node = result is JsonElement element
                ? JsonNode.Parse(element.GetRawText())
                : JsonSerializer.SerializeToNode(result, result.GetType(), JsonOptions);
            return RedactNode(node);
        }
        catch (Exception exception) when (CancellationExceptions.Unwrap(exception) is { } canceled)
        {
            throw canceled;
        }
        catch (Exception exception)
        {
            var safeMessage = RedactText(exception.Message);
            throw new InvalidOperationException(safeMessage);
        }
    }

    private JsonNode? RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
                        jsonObject[property.Key] = JsonValue.Create(RedactText(text));
                    else
                        RedactNode(property.Value);
                }
                return jsonObject;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    if (jsonArray[index] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
                        jsonArray[index] = JsonValue.Create(RedactText(text));
                    else
                        RedactNode(jsonArray[index]);
                }
                return jsonArray;
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text):
                return JsonValue.Create(RedactText(text));
            default:
                return node;
        }
    }

    private string RedactText(string value)
    {
        foreach (var secret in _secrets)
            value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        return value;
    }
}
