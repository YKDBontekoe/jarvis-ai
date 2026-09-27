using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            // The Agent Framework's serializers require $type, then $id, before ordinary properties.
            foreach (var property in element.EnumerateObject().OrderBy(p => MetadataSortKey(p.Name)))
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

    public static bool TryGetCompletedAssistantText(string sessionJson, out string text)
    {
        text = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!TryFindMessages(document.RootElement, out var messages) || messages.GetArrayLength() < 2)
                return false;

            var last = messages[messages.GetArrayLength() - 1];
            if (IsRole(last, "user") || !TryGetPlainText(last, out text))
                return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetCompletedAssistantTextAfterUser(string sessionJson, string userContent, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(userContent)) return false;
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!TryFindMessages(document.RootElement, out var messages) || messages.GetArrayLength() < 2)
                return false;

            var lastIndex = messages.GetArrayLength() - 1;
            var last = messages[lastIndex];
            if (IsRole(last, "user") || !TryGetPlainText(last, out text))
                return false;

            for (var index = lastIndex - 1; index >= 0; index--)
            {
                var message = messages[index];
                if (IsRole(message, "assistant") || HasToolContents(message))
                    continue;
                if (!TryGetPlainText(message, out var previousText))
                {
                    text = string.Empty;
                    return false;
                }
                if (string.Equals(previousText, userContent, StringComparison.Ordinal))
                    return true;
                text = string.Empty;
                return false;
            }

            text = string.Empty;
            return false;
        }
        catch (JsonException)
        {
            text = string.Empty;
            return false;
        }
    }

    public static bool HasInFlightProgressAfterUser(string sessionJson, string userContent)
    {
        if (string.IsNullOrEmpty(userContent)) return false;
        if (TryGetCompletedAssistantTextAfterUser(sessionJson, userContent, out _))
            return false;
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!TryFindMessages(document.RootElement, out var messages) || messages.GetArrayLength() == 0)
                return false;

            var lastUserIndex = -1;
            for (var index = 0; index < messages.GetArrayLength(); index++)
            {
                var message = messages[index];
                if (IsRole(message, "assistant")) continue;
                if (!TryGetPlainText(message, out var text) ||
                    !string.Equals(text, userContent, StringComparison.Ordinal))
                    continue;
                lastUserIndex = index;
            }

            if (lastUserIndex < 0) return false;
            for (var index = lastUserIndex + 1; index < messages.GetArrayLength(); index++)
            {
                if (!IsRole(messages[index], "assistant") && TryGetPlainText(messages[index], out _))
                    return false;
            }

            if (lastUserIndex == messages.GetArrayLength() - 1)
                return true;

            for (var index = lastUserIndex + 1; index < messages.GetArrayLength(); index++)
            {
                if (HasToolContents(messages[index])) return true;
                if (IsRole(messages[index], "assistant") && !TryGetPlainText(messages[index], out _))
                    return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryAbandonIncompleteTurn(string sessionJson, string incomingUserContent, out string truncated)
    {
        truncated = sessionJson;
        if (string.IsNullOrEmpty(incomingUserContent) ||
            !TryGetLastUserText(sessionJson, out var lastUser) ||
            string.Equals(lastUser, incomingUserContent, StringComparison.Ordinal) ||
            !HasInFlightProgressAfterUser(sessionJson, lastUser))
            return false;
        return TryDropIncompleteTurn(sessionJson, out truncated);
    }

    public static bool TryGetPendingApprovals(string sessionJson,
        out IReadOnlyList<AgentToolApprovalRequest> approvals, out string preface)
    {
        approvals = [];
        preface = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!TryFindMessages(document.RootElement, out var messages) || messages.GetArrayLength() == 0)
                return false;

            var last = messages[messages.GetArrayLength() - 1];
            if (IsRole(last, "user") ||
                !last.TryGetProperty("contents", out var contents) ||
                contents.ValueKind != JsonValueKind.Array)
                return false;

            var found = new List<AgentToolApprovalRequest>();
            var text = new StringBuilder();
            foreach (var content in contents.EnumerateArray())
            {
                var type = content.TryGetProperty("$type", out var typeElement) ? typeElement.GetString() : null;
                if (type is "text")
                {
                    if (content.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String)
                        text.Append(textElement.GetString());
                    continue;
                }

                if (type is not ("functionApprovalRequest" or "toolApprovalRequest")) continue;
                if (TryReadApproval(content, out var request))
                    found.Add(request);
            }

            if (found.Count == 0) return false;
            approvals = found;
            preface = text.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool HasAnsweredApproval(string sessionJson, string requestId, string toolCallId)
    {
        if (string.IsNullOrEmpty(requestId) && string.IsNullOrEmpty(toolCallId)) return false;
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!TryFindMessages(document.RootElement, out var messages))
                return false;

            for (var index = 0; index < messages.GetArrayLength(); index++)
            {
                var message = messages[index];
                if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var content in contents.EnumerateArray())
                {
                    var type = content.TryGetProperty("$type", out var typeElement) ? typeElement.GetString() : null;
                    if (type is "toolApproval" or "functionApproval"
                        or "toolApprovalResponse" or "functionApprovalResponse")
                    {
                        var id = ReadString(content, "id") ?? ReadString(content, "requestId");
                        if (string.Equals(id, requestId, StringComparison.Ordinal))
                            return true;
                    }
                    else if (type is "functionCall" or "functionResult")
                    {
                        var callId = ReadString(content, "callId");
                        if (string.Equals(callId, toolCallId, StringComparison.Ordinal))
                            return true;
                    }
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetLastUserText(string sessionJson, out string text)
    {
        text = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!TryFindMessages(document.RootElement, out var messages) || messages.GetArrayLength() == 0)
                return false;

            var found = false;
            for (var index = 0; index < messages.GetArrayLength(); index++)
            {
                var message = messages[index];
                if (IsRole(message, "assistant")) continue;
                if (!TryGetPlainText(message, out var userText)) continue;
                text = userText;
                found = true;
            }

            return found;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryDropIncompleteTurn(string sessionJson, out string truncated)
    {
        truncated = sessionJson;
        try
        {
            var node = JsonNode.Parse(sessionJson);
            if (node is null || !TryFindMessagesNode(node, out var messages) || messages.Count == 0)
                return false;

            var removed = false;
            while (messages.Count > 0 && !IsCompletedAssistantNode(messages[^1]))
            {
                messages.RemoveAt(messages.Count - 1);
                removed = true;
            }

            if (!removed) return false;
            truncated = PrepareForRead(node.ToJsonString());
            return true;
        }
        catch (JsonException)
        {
            truncated = sessionJson;
            return false;
        }
    }

    private static bool TryFindMessagesNode(JsonNode node, out JsonArray messages)
    {
        messages = null!;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("stateBag", out var stateBag) && stateBag is not null &&
                TryFindMessagesNode(stateBag, out messages))
                return true;
            if (obj.TryGetPropertyValue("messages", out var value) && value is JsonArray array)
            {
                messages = array;
                return true;
            }

            foreach (var property in obj)
            {
                if (property.Key is "stateBag" or "messages" || property.Value is null) continue;
                if (TryFindMessagesNode(property.Value, out messages)) return true;
            }
        }
        else if (node is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is not null && TryFindMessagesNode(item, out messages)) return true;
            }
        }

        return false;
    }

    private static bool IsCompletedAssistantNode(JsonNode? node)
    {
        if (node is null) return false;
        using var document = JsonDocument.Parse(node.ToJsonString());
        var message = document.RootElement;
        return !IsRole(message, "user") && TryGetPlainText(message, out _) && !HasToolContents(message);
    }

    private static bool TryReadApproval(JsonElement content, out AgentToolApprovalRequest request)
    {
        request = null!;
        var requestId = ReadString(content, "id") ?? ReadString(content, "requestId");
        if (string.IsNullOrEmpty(requestId) ||
            !(content.TryGetProperty("functionCall", out var call) || content.TryGetProperty("toolCall", out call)) ||
            call.ValueKind != JsonValueKind.Object)
            return false;

        var callId = ReadString(call, "callId");
        var name = ReadString(call, "name");
        if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(name))
            return false;

        var argumentsJson = "{}";
        if (call.TryGetProperty("arguments", out var args))
        {
            argumentsJson = args.ValueKind == JsonValueKind.String
                ? args.GetString() ?? "{}"
                : args.GetRawText();
        }

        request = new AgentToolApprovalRequest(requestId, callId, name, argumentsJson);
        return true;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsRole(JsonElement message, string role) =>
        message.TryGetProperty("role", out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), role, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPlainText(JsonElement message, out string text)
    {
        text = string.Empty;
        if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
            return false;

        var output = new StringBuilder();
        foreach (var content in contents.EnumerateArray())
        {
            var type = content.TryGetProperty("$type", out var typeElement) ? typeElement.GetString() : null;
            if (type is "functionCall" or "functionResult" or "functionApprovalRequest" or "toolApprovalRequest"
                or "toolApproval" or "functionApproval" or "toolApprovalResponse" or "functionApprovalResponse")
                return false;
            if (content.TryGetProperty("text", out var textElement) &&
                (type is null or "text") &&
                textElement.ValueKind == JsonValueKind.String)
            {
                output.Append(textElement.GetString());
                continue;
            }
            return false;
        }

        text = output.ToString();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool HasToolContents(JsonElement message)
    {
        if (!message.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var content in contents.EnumerateArray())
        {
            var type = content.TryGetProperty("$type", out var typeElement) ? typeElement.GetString() : null;
            if (type is "functionCall" or "functionResult" or "functionApprovalRequest" or "toolApprovalRequest"
                or "toolApproval" or "functionApproval" or "toolApprovalResponse" or "functionApprovalResponse")
                return true;
        }
        return false;
    }

    private static int MetadataSortKey(string name) => name switch
    {
        "$type" => 0,
        "$id" => 1,
        _ when name.StartsWith('$') => 2,
        _ => 3
    };

    private static bool TryFindMessages(JsonElement element, out JsonElement messages)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("stateBag", out var stateBag) &&
                TryFindMessages(stateBag, out messages))
                return true;
            if (element.TryGetProperty("messages", out messages) && messages.ValueKind == JsonValueKind.Array)
                return true;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "stateBag" or "messages") continue;
                if (TryFindMessages(property.Value, out messages)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindMessages(item, out messages)) return true;
            }
        }

        messages = default;
        return false;
    }
}
