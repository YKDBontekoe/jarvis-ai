using System.Text.Json;

namespace Jarvis.Api.Errors;

internal static class LegacyErrorResponseParser
{
    public static bool AlreadyProblemContract(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return false;
        try
        {
            using var document = JsonDocument.Parse(bodyText);
            return document.RootElement.TryGetProperty("code", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? ExtractClientDetail(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return null;
        try
        {
            using var document = JsonDocument.Parse(bodyText);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString();
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                return title.GetString();
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool TryParseValidationErrors(string? bodyText, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(bodyText)) return false;
        try
        {
            using var document = JsonDocument.Parse(bodyText);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement) ||
                errorsElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in errorsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;
                var messages = property.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? "")
                    .Where(message => message.Length > 0)
                    .ToArray();
                if (messages.Length > 0) errors[property.Name] = messages;
            }
        }
        catch (JsonException)
        {
            errors.Clear();
            return false;
        }

        return errors.Count > 0;
    }
}
