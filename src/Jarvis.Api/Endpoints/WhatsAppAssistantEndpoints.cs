using System.Text.Json;
using Jarvis.Api.Channels;
using Jarvis.Api.Conversations;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.WhatsApp;

namespace Jarvis.Api.Endpoints;

/// <param name="Live">False when the chat list could not be read from the phone and only saved chats are shown.</param>
public sealed record WhatsAppChatListDto(bool Live, IReadOnlyList<WhatsAppChatDto> Chats,
    string Account, string State);

public sealed record WhatsAppChatDto(string ChatId, string Name, bool IsGroup, DateTimeOffset? LastMessageAt,
    bool ReadAlong, bool AutoReminders, string? Preview = null, bool? PreviewFromMe = null, int UnreadCount = 0);

public sealed record WhatsAppConnectionDto(string Account, string State);
public sealed record WhatsAppMarkReadRequest(Guid MessageId);

public sealed record SaveWhatsAppChatRequest(string? Name, bool ReadAlong, bool? AutoReminders);

public sealed record WhatsAppMessageDto(Guid Id, string ChatId, bool FromMe, string? Sender, string Text,
    DateTimeOffset SentAt, DateTimeOffset? ReceivedAt = null);

public sealed record WhatsAppSuggestRequest(string? Instruction);

public sealed record WhatsAppSuggestionDto(string Text);

public sealed record WhatsAppSendRequest(string? Text);

public sealed record WhatsAppAskRequest(string? Question);

/// <param name="Answer">Jarvis' answer, or null when it is waiting for an approval in the app.</param>
public sealed record WhatsAppAskDto(Guid ConversationId, string? Answer, bool NeedsApproval);

/// <summary>
/// "Read along" on the owner's own linked WhatsApp: pick chats, see their messages, get a drafted reply, and send
/// one by tapping Send. Audit entries carry ids only, never names, numbers, or message text.
/// </summary>
internal static class WhatsAppAssistantEndpoints
{
    public static RouteGroupBuilder MapWhatsAppAssistantEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/channels/{id:guid}/chats");

        group.MapGet("", async (Guid id, IChannelRepository channels, IWhatsAppAssistantRepository chats,
            WhatsAppBridgeClient bridge, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var connection = await LinkedAsync(channels, currentUser.OwnerId, id, ct);
            if (connection is null) return Results.NotFound();
            var saved = (await chats.ListChatsAsync(currentUser.OwnerId, id, ct)).ToDictionary(x => x.ChatId);
            IReadOnlyList<BridgeChat> phone = [];
            var state = await ConnectionStateAsync(connection, bridge, ct);
            if (bridge.Configured)
            {
                try
                {
                    phone = await bridge.ListChatsAsync(id, ct);
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                                      or JsonException)
                {
                    if (state == "open") state = "unreachable";
                    logger.LogDebug(exception, "Could not read the WhatsApp chat list for {ConnectionId}.", id);
                }
            }
            var activity = (await chats.ListActivityAsync(currentUser.OwnerId, id, ct)).ToDictionary(x => x.ChatId);
            var merged = Merge(saved, phone).Select(chat => activity.TryGetValue(chat.ChatId, out var item)
                ? chat with { Preview = item.Preview, PreviewFromMe = item.FromMe, UnreadCount = item.UnreadCount }
                : chat).ToArray();
            return Results.Ok(new WhatsAppChatListDto(state == "open", merged, connection.Account, state));
        }).WithName("ListWhatsAppChats");

        group.MapGet("/status", async (Guid id, IChannelRepository channels, WhatsAppBridgeClient bridge,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var connection = await LinkedAsync(channels, currentUser.OwnerId, id, ct);
            return connection is null ? Results.NotFound() : Results.Ok(new WhatsAppConnectionDto(connection.Account,
                await ConnectionStateAsync(connection, bridge, ct)));
        }).WithName("GetWhatsAppConnectionStatus");

        group.MapPost("/{chatId}/read", async (Guid id, string chatId, WhatsAppMarkReadRequest request,
            IWhatsAppAssistantRepository chats, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var normalized = WhatsAppChatIds.Normalize(chatId);
            return normalized is not null && await chats.MarkReadAsync(currentUser.OwnerId, id, normalized,
                request.MessageId, ct) ? Results.NoContent() : Results.NotFound();
        }).WithName("MarkWhatsAppChatRead");

        group.MapPut("/{chatId}", async (Guid id, string chatId, SaveWhatsAppChatRequest request,
            IChannelRepository channels, IWhatsAppAssistantRepository chats, WhatsAppReadAlongReceiver receiver,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var normalized = WhatsAppChatIds.Normalize(chatId);
            if (normalized is null) return EndpointHelpers.Invalid("chatId", "That is not a WhatsApp chat.");
            var connection = await LinkedAsync(channels, currentUser.OwnerId, id, ct);
            if (connection is null) return Results.NotFound();
            if (normalized == ChannelAddresses.Normalize(connection.Account))
                return EndpointHelpers.Invalid("chatId",
                    "This is your own chat with Jarvis. It is always on and Jarvis answers there.");
            var before = await chats.GetChatAsync(currentUser.OwnerId, id, normalized, ct);
            var saved = await chats.SaveChatAsync(currentUser.OwnerId, id, normalized,
                request.Name ?? before?.DisplayName ?? "", request.ReadAlong,
                request.AutoReminders ?? before?.AutoReminders ?? true, ct);
            if (saved is null) return Results.NotFound();
            try
            {
                await receiver.SyncWatchAsync(id, await chats.ListWatchedChatIdsAsync(id, ct), ct);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // The receiver retries every few seconds; the saved choice already decides what gets stored.
                logger.LogDebug(exception, "Could not update the WhatsApp watch list right away.");
            }
            if (before?.ReadAlong != saved.ReadAlong || before?.AutoReminders != saved.AutoReminders)
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "whatsapp",
                    "whatsapp.read_along_changed", "moderate", true, null, JsonSerializer.Serialize(new
                    {
                        resourceId = saved.Id, connectionId = id, readAlong = saved.ReadAlong,
                        autoReminders = saved.AutoReminders
                    }), ct);
            return Results.Ok(ToDto(saved));
        }).WithName("SaveWhatsAppChat");

        group.MapGet("/{chatId}/messages", async (Guid id, string chatId, DateTimeOffset? before, Guid? beforeId, int? limit,
            IWhatsAppAssistantRepository chats, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var chat = await FindAsync(chats, currentUser.OwnerId, id, chatId, ct);
            if (chat is null) return Results.NotFound();
            if (before is not null && beforeId is not null)
                return EndpointHelpers.Invalid("beforeId", "Choose one message cursor.");
            var messages = await chats.ListMessagesAsync(currentUser.OwnerId, id, chat.ChatId,
                Math.Clamp(limit ?? 60, 1, 200), before, ct, beforeId);
            return Results.Ok(messages.Select(ToDto).ToArray());
        }).WithName("ListWhatsAppChatMessages");

        group.MapDelete("/{chatId}/messages", async (Guid id, string chatId, IWhatsAppAssistantRepository chats,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var chat = await FindAsync(chats, currentUser.OwnerId, id, chatId, ct);
            if (chat is null) return Results.NotFound();
            var removed = await chats.ClearHistoryAsync(currentUser.OwnerId, id, chat.ChatId, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "whatsapp",
                "whatsapp.history_cleared", "moderate", true, null,
                JsonSerializer.Serialize(new { resourceId = chat.Id, removed }), ct);
            return Results.NoContent();
        }).WithName("ClearWhatsAppChatHistory");

        group.MapPost("/{chatId}/suggest", async (Guid id, string chatId, WhatsAppSuggestRequest request,
            IWhatsAppAssistantRepository chats, IWhatsAppAssistant assistant, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var chat = await FindAsync(chats, currentUser.OwnerId, id, chatId, ct);
            if (chat is not { ReadAlong: true }) return Results.NotFound();
            var messages = (await chats.ListMessagesAsync(currentUser.OwnerId, id, chat.ChatId, 30, null, ct))
                .Reverse().ToArray();
            if (messages.Length == 0)
                return EndpointHelpers.Invalid("chat", "There are no messages in this chat yet to answer.");
            var instruction = request.Instruction?.Trim();
            var draft = await assistant.SuggestReplyAsync(currentUser.OwnerId, chat, messages,
                string.IsNullOrEmpty(instruction) ? null : instruction, ct);
            return draft is null
                ? ApiProblemResults.BadGateway("Jarvis could not write a reply right now. Try again.")
                : Results.Ok(new WhatsAppSuggestionDto(draft));
        }).WithName("SuggestWhatsAppReply");

        group.MapPost("/{chatId}/send", async (Guid id, string chatId, WhatsAppSendRequest request,
            IWhatsAppAssistantRepository chats, IWhatsAppSender sender, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var text = request.Text?.Trim() ?? "";
            if (text.Length == 0) return EndpointHelpers.Invalid("text", "Write a message first.");
            if (text.Length > WhatsAppChatIds.MaxMessageLength)
                return EndpointHelpers.Invalid("text", "That message is too long for WhatsApp.");
            var chat = await FindAsync(chats, currentUser.OwnerId, id, chatId, ct);
            if (chat is null) return Results.NotFound();
            var result = await sender.SendAsync(currentUser.OwnerId, id, chat.ChatId, text, ct);
            return result.Sent
                ? Results.Ok(new { sent = true })
                : Results.Conflict(new { message = result.Error ?? "The message was not sent." });
        }).WithName("SendWhatsAppMessage");

        group.MapPost("/{chatId}/ask", async (Guid id, string chatId, WhatsAppAskRequest request,
            IWhatsAppAssistantRepository chats, IConversationStore conversations, RemoteQueryExecutor remote,
            ICurrentUser currentUser) =>
        {
            var question = request.Question?.Trim() ?? "";
            if (question.Length is 0 or > 4_000) return EndpointHelpers.Invalid("question", "Ask a short question.");
            // The request abort token is intentionally unused, like chat: the answer is stored even if the phone
            // drops the connection, and the conversation stays available in Jarvis.
            var ct = CancellationToken.None;
            var chat = await FindAsync(chats, currentUser.OwnerId, id, chatId, ct);
            if (chat is not { ReadAlong: true }) return Results.NotFound();
            var prompt = AskPrompt(chat, question);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var conversationId = chat.AskConversationId is { } existing &&
                                     await conversations.GetAsync(existing, currentUser.OwnerId, ct) is not null
                    ? existing
                    : (await conversations.CreateAsync(currentUser.OwnerId,
                        $"WhatsApp · {AgentSafeTitle(chat.DisplayName)}", ct)).Id;
                if (conversationId != chat.AskConversationId)
                {
                    await chats.SetAskConversationAsync(currentUser.OwnerId, chat.Id, conversationId, ct);
                    chat = chat with { AskConversationId = conversationId };
                }
                ConversationTurnResult result;
                try
                {
                    result = await remote.SendAsync(currentUser.OwnerId, conversationId, prompt);
                }
                catch (OperationCanceledException)
                {
                    return ApiProblemResults.Failed("Jarvis took too long to answer. Try again.");
                }
                switch (result)
                {
                    case ConversationTurnResult.Completed completed:
                        return Results.Ok(new WhatsAppAskDto(conversationId, completed.Message.Content, false));
                    case ConversationTurnResult.AwaitingApproval:
                        return Results.Ok(new WhatsAppAskDto(conversationId, null, true));
                    case ConversationTurnResult.NotFound:
                        chat = chat with { AskConversationId = null };
                        continue;
                    default:
                        return result.ToHttpResult();
                }
            }
            return ApiProblemResults.Failed("Jarvis could not start a conversation for this chat.");
        }).WithName("AskJarvisAboutWhatsAppChat");

        return api;
    }

    /// <summary>The question plus which chat it is about; the agent reads the chat itself with its tools.</summary>
    internal static string AskPrompt(WhatsAppChatSettings chat, string question)
    {
        var who = chat.IsGroup ? $"the WhatsApp group \"{chat.DisplayName}\"" : $"my WhatsApp chat with \"{chat.DisplayName}\"";
        var id = chat.ChatId.StartsWith('+') ? $" ({chat.ChatId})" : "";
        return $"About {who}{id}. Read it with ReadWhatsAppChat if you need to; do not send anything unless I ask.\n\n{question}";
    }

    private static string AgentSafeTitle(string name) => name.Length <= 60 ? name : name[..60].TrimEnd();

    /// <summary>
    /// Phone chats first by recent activity, with the owner's saved choices on top of them; saved chats the phone
    /// no longer lists stay visible so a chat that is on can always be turned off.
    /// </summary>
    internal static IReadOnlyList<WhatsAppChatDto> Merge(IReadOnlyDictionary<string, WhatsAppChatSettings> saved,
        IReadOnlyList<BridgeChat> phone)
    {
        var result = new Dictionary<string, WhatsAppChatDto>();
        foreach (var chat in phone)
        {
            var chatId = WhatsAppChatIds.Normalize(chat.Id);
            if (chatId is null || result.ContainsKey(chatId)) continue;
            saved.TryGetValue(chatId, out var settings);
            DateTimeOffset? last = chat.LastMessageAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(chat.LastMessageAt) : null;
            if (settings?.LastMessageAt is { } stored && (last is null || stored > last)) last = stored;
            var name = settings is not null && settings.DisplayName != WhatsAppChatIds.FallbackName(chatId)
                ? settings.DisplayName
                : WhatsAppChatIds.CleanName(chat.Name, chatId);
            result[chatId] = new WhatsAppChatDto(chatId, name, WhatsAppChatIds.IsGroup(chatId), last,
                settings?.ReadAlong ?? false, settings?.AutoReminders ?? true);
        }
        foreach (var settings in saved.Values)
            if (!result.ContainsKey(settings.ChatId)) result[settings.ChatId] = ToDto(settings);
        return result.Values
            .OrderByDescending(x => x.ReadAlong)
            .ThenByDescending(x => x.LastMessageAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string> ConnectionStateAsync(ChannelConnectionRecord connection,
        WhatsAppBridgeClient bridge, CancellationToken ct)
    {
        if (!connection.Enabled) return "paused";
        if (!bridge.Configured) return "unavailable";
        try { return (await bridge.GetStatusAsync(connection.Id, ct)).State; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        { return "unreachable"; }
    }

    private static async Task<ChannelConnectionRecord?> LinkedAsync(IChannelRepository channels, Guid ownerId,
        Guid id, CancellationToken ct) =>
        await channels.GetAsync(ownerId, id, ct) is { Kind: ChannelKinds.WhatsAppLinked } connection
            ? connection
            : null;

    private static async Task<WhatsAppChatSettings?> FindAsync(IWhatsAppAssistantRepository chats, Guid ownerId,
        Guid id, string chatId, CancellationToken ct) =>
        WhatsAppChatIds.Normalize(chatId) is { } normalized
            ? await chats.GetChatAsync(ownerId, id, normalized, ct)
            : null;

    private static WhatsAppChatDto ToDto(WhatsAppChatSettings chat) =>
        new(chat.ChatId, chat.DisplayName, chat.IsGroup, chat.LastMessageAt, chat.ReadAlong, chat.AutoReminders);

    private static WhatsAppMessageDto ToDto(WhatsAppChatMessage message) =>
        new(message.Id, message.ChatId, message.FromMe, message.Sender, message.Text, message.SentAt, message.ReceivedAt);
}
