using System.Globalization;
using Jarvis.Application.Channels;
using Jarvis.Application.Search;
using Jarvis.Application.WhatsApp;

namespace Jarvis.Application.Navigation;

/// <summary>Resolves model intent to actual owner-scoped resources. Planning has no side effects.</summary>
public sealed class NavigationService(INavigationIntentInterpreter interpreter, IFederatedSearchService search,
    IWhatsAppAssistantRepository chats, IChannelRepository channels)
{
    public async Task<NavigationResponse> ResolveAsync(Guid ownerId, string request, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerId, Guid.Empty);
        request = request.Trim();
        if (request.Length is < 1 or > 1_000) throw new ArgumentException("Use 1 to 1,000 characters.", nameof(request));
        var intent = await interpreter.InterpretAsync(ownerId, request, ct);
        if (intent is null) return new("I couldn't work out the next step. You can ask Jarvis or browse the tools.",
            [Ask(request)], false);

        switch (intent.Kind)
        {
            case "open":
                var destination = NavigationCatalog.Destinations.FirstOrDefault(item => item.Id == intent.Destination);
                return destination is null
                    ? new("Jarvis can help work through this request.", [Ask(request)], false)
                    : new("Here's where you can do that.", [NavigationCatalog.Open(destination)]);
            case "whatsapp":
            case "reply":
            case "ask_whatsapp":
                return await ResolveChatAsync(ownerId, request, intent, ct);
            case "find":
                if (string.IsNullOrWhiteSpace(intent.Query)) break;
                var found = await search.SearchAsync(ownerId, intent.Query, null, ct);
                var actions = found.Results.Take(6).Select(hit => new NavigationAction(hit.Title,
                    hit.Summary ?? hit.Kind, hit.Route)).ToArray();
                return actions.Length == 0
                    ? new("I couldn't find a saved match. Jarvis can help you look further.", [Ask(request)])
                    : new("These saved items match what you're looking for.", actions);
            case "assistant":
                return new("Jarvis can work on this with you.", [Ask(request)]);
        }
        return new("You can work through this with Jarvis.", [Ask(request)], false);
    }

    public async Task<NavigationResponse> SuggestionsAsync(Guid ownerId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerId, Guid.Empty);
        var connections = (await channels.ListAsync(ownerId, ct))
            .Where(item => item.OwnerId == ownerId && item.Kind == ChannelKinds.WhatsAppLinked).ToDictionary(item => item.Id);
        var watched = (await chats.ListChatsAsync(ownerId, null, ct))
            .Where(item => item.OwnerId == ownerId && item.ReadAlong && connections.ContainsKey(item.ConnectionId))
            .ToDictionary(item => (item.ConnectionId, item.ChatId));
        var unread = new List<(WhatsAppChatSettings Chat, int Count)>();
        foreach (var connection in connections.Values)
        {
            foreach (var activity in await chats.ListActivityAsync(ownerId, connection.Id, ct))
                if (activity.UnreadCount > 0 && watched.TryGetValue((connection.Id, activity.ChatId), out var chat))
                    unread.Add((chat, activity.UnreadCount));
        }
        var actions = unread.OrderByDescending(item => item.Chat.LastMessageAt).Take(3)
            .Select(item => new NavigationAction($"Catch up with {item.Chat.DisplayName}",
                $"{item.Count} unread in Jarvis · {connections[item.Chat.ConnectionId].Account}",
                ChatRoute(item.Chat, null, null))).ToList();
        actions.Add(NavigationCatalog.Open(NavigationCatalog.Destinations[0]));
        return new("A good place to start", actions);
    }

    private async Task<NavigationResponse> ResolveChatAsync(Guid ownerId, string request, NavigationIntent intent,
        CancellationToken ct)
    {
        var connections = (await channels.ListAsync(ownerId, ct))
            .Where(item => item.OwnerId == ownerId && item.Kind == ChannelKinds.WhatsAppLinked).ToDictionary(item => item.Id);
        var query = intent.Query?.Trim() ?? "";
        if (query.Length == 0 && intent.Kind == "whatsapp")
            return new("Your WhatsApp conversations.", [NavigationCatalog.Open(NavigationCatalog.Destinations[1])]);
        var matches = (await chats.ListChatsAsync(ownerId, null, ct))
            .Where(chat => chat.OwnerId == ownerId && chat.ReadAlong && connections.ContainsKey(chat.ConnectionId) &&
                           (query.Length == 0 || Matches(chat, query)))
            .OrderByDescending(chat => chat.LastMessageAt).ToArray();
        if (matches.Length == 0)
            return new("No selected WhatsApp chat matches yet. Connect your account or choose the chat first.",
                [NavigationCatalog.Open(NavigationCatalog.Destinations[1])]);
        var workflow = intent.Kind == "reply" ? "draft" : intent.Kind == "ask_whatsapp" ? "ask" : null;
        var actions = matches.Take(6).Select(chat => new NavigationAction(
            workflow == "draft" ? $"Draft a reply to {chat.DisplayName}" :
            workflow == "ask" ? $"Ask about {chat.DisplayName}" : $"Open {chat.DisplayName}",
            $"{connections[chat.ConnectionId].DisplayName} · {connections[chat.ConnectionId].Account}",
            ChatRoute(chat, workflow, workflow is null ? null : request))).ToArray();
        return new(matches.Length == 1 ? "Here's the conversation you need." :
            matches.Length > 6 ? "Several chats match. Here are the six most recent; include a name or number to narrow it down." :
            "Which conversation do you mean?", actions);
    }

    private static bool Matches(WhatsAppChatSettings chat, string query) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(chat.DisplayName, query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0 ||
        chat.ChatId.Equals(query, StringComparison.OrdinalIgnoreCase) ||
        (query.Any(char.IsDigit) && query.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || "+()-".Contains(c)) &&
         chat.ChatId == ChannelAddresses.Normalize(query));

    private static SearchRouteTarget ChatRoute(WhatsAppChatSettings chat, string? workflow, string? request)
    {
        var parameters = new Dictionary<string, string>
        {
            ["connectionId"] = chat.ConnectionId.ToString(), ["chatId"] = chat.ChatId
        };
        if (workflow is not null) parameters["workflow"] = workflow;
        if (request is not null) parameters["request"] = request;
        return new("whatsapp_chat", parameters);
    }

    private static NavigationAction Ask(string request) => new("Work on this with Jarvis",
        "Continue in chat with your request", new SearchRouteTarget("assistant",
            new Dictionary<string, string> { ["prompt"] = request }));
}
