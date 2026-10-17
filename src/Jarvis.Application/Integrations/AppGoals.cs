using Jarvis.Application.Settings;

namespace Jarvis.Application.Integrations;

/// <summary>A harmless first use of a connected app. Reads only; nothing is created, sent or changed.</summary>
public sealed record AppGoalTry(string Label, string Kind, string? Prompt);

public static class AppTryKinds
{
    /// <summary>Jarvis reads today's events itself and shows them.</summary>
    public const string CalendarToday = "calendar_today";

    /// <summary>Opens a chat with a read-only request, so the app's own approvals apply.</summary>
    public const string ChatPrompt = "chat_prompt";
}

/// <summary>A job the owner wants help with, tied to the integration pack that can do it.</summary>
public sealed record AppGoal(
    string Id,
    string Title,
    string Purpose,
    string PackId,
    IReadOnlyList<string> Keywords,
    AppGoalTry Try);

/// <summary>What Jarvis could read, prepare and change through the app, derived from its actual tools.</summary>
public sealed record AppCapabilityPreview(
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Prepares,
    IReadOnlyList<string> Changes,
    string Needs);

public static class AppGoalStatuses
{
    public const string NotConnected = "not_connected";
    public const string Ready = "ready";
}

public sealed record AppGoalView(
    string Id,
    string Title,
    string Purpose,
    string PackId,
    string PackName,
    string Status,
    string NextStep,
    AppCapabilityPreview Capabilities,
    AppGoalTry? Try,
    bool Pinned);

public sealed record AppGoalSearch(IReadOnlyList<AppGoalView> Goals, bool Unmatched, string? Suggestion);

/// <summary>Today's events, read for real, or why that was not possible.</summary>
public sealed record AppTryResult(string Kind, IReadOnlyList<CalendarEventRecord> Events, string? Prompt,
    string? Problem = null);

public static class AppGoalCatalog
{
    public static readonly IReadOnlyList<AppGoal> All =
    [
        new("calendar", "Manage my calendar",
            "See what is on today and plan around it. Jarvis reads your events; adding one asks you first.",
            IntegrationPackIds.Calendar,
            ["calendar", "agenda", "events", "event", "schedule", "appointment", "appointments", "meeting", "today",
             "kalender", "afspraak", "afspraken", "planning", "vandaag"],
            new AppGoalTry("What is on today?", AppTryKinds.CalendarToday, null)),
        new("mail", "Deal with my email",
            "Search your mail and draft replies for you to send. You sign in once; Jarvis never asks for your password in chat.",
            IntegrationPackIds.Mail,
            ["mail", "email", "e-mail", "inbox", "reply", "message", "messages", "post", "bericht", "berichten",
             "antwoord"],
            new AppGoalTry("Show my latest emails", AppTryKinds.ChatPrompt,
                "Show the sender and subject of my last 5 emails. Only read them; do not send, delete or change anything.")),
        new("contacts", "Look up people I know",
            "Find a phone number, email or details of people in your contacts.",
            IntegrationPackIds.Contacts,
            ["contacts", "contact", "people", "phone", "number", "address book", "contacten", "telefoonnummer",
             "adresboek"],
            new AppGoalTry("Find a contact", AppTryKinds.ChatPrompt,
                "Look up one of my contacts by name and show their details. Only read; do not change anything."))
    ];

    /// <summary>Goals whose title, purpose or keywords share a word with the search; every goal for an empty search.</summary>
    public static IReadOnlyList<AppGoal> Match(string? query)
    {
        var words = (query ?? string.Empty).ToLowerInvariant()
            .Split([' ', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3).ToArray();
        if (words.Length == 0) return All;
        return All.Where(goal => words.Any(word =>
            goal.Keywords.Any(keyword => keyword == word || (word.Length >= 4 && keyword.StartsWith(word,
                StringComparison.Ordinal))) ||
            goal.Title.Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    /// <summary>Plain-language capabilities worked out from the pack's tool names, never invented.</summary>
    public static AppCapabilityPreview Preview(IntegrationPack pack)
    {
        var reads = new List<string>();
        var prepares = new List<string>();
        var changes = new List<string>();
        foreach (var tool in pack.SuggestedTools)
        {
            var name = tool.ToLowerInvariant();
            var label = tool.Replace('_', ' ');
            if (name.Contains("draft")) prepares.Add(label);
            else if (name.Contains("create") || name.Contains("send") || name.Contains("delete") ||
                     name.Contains("update") || name.Contains("add_")) changes.Add(label);
            else reads.Add(label);
        }
        var needs = pack.AuthKind switch
        {
            "ics" => "A private calendar link from your calendar provider.",
            "oauth" => "You sign in once with your provider. Jarvis never sees your password.",
            _ => "An address for the service."
        };
        if (pack.SuggestedCommand is null && pack.SuggestedEndpoint is null)
            needs += " Jarvis has no ready-made connector for this yet, so you need an MCP address for your provider.";
        return new AppCapabilityPreview(reads, prepares, changes, needs);
    }

    public static string NextStep(IntegrationPack pack, bool ready) => ready
        ? "Connected. Try it below or pin it."
        : pack.AuthKind switch
        {
            "ics" => "Paste your calendar's private link to connect.",
            "oauth" when pack.SuggestedCommand is not null => "Connect and sign in once with your provider.",
            _ => "Add the MCP address of your provider to connect. Jarvis has no ready-made connector for this one."
        };

    public const string BuildSuggestion =
        "No app Jarvis knows covers that yet. It can build a small one for you from a service's documented API, " +
        "or you can add an MCP server by address. Nothing has been connected.";
}

/// <summary>Pinned app actions: the owner's chosen goals, kept in settings (<c>apps.pins</c>).</summary>
public sealed record AppPinState(IReadOnlyList<string> Goals)
{
    public const string Section = "apps.pins";
    public const int Max = 6;
}

public interface IAppGoalService
{
    Task<AppGoalSearch> SearchAsync(Guid ownerId, string? query, CancellationToken cancellationToken);

    /// <summary>Runs the harmless first use. Null when the goal is unknown or the app is not connected yet.</summary>
    Task<AppTryResult?> TryAsync(Guid ownerId, string goalId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>False when the goal is unknown or its app is not connected, or the pin limit is reached.</summary>
    Task<bool> PinAsync(Guid ownerId, string goalId, CancellationToken cancellationToken);

    Task<bool> UnpinAsync(Guid ownerId, string goalId, CancellationToken cancellationToken);
}

public sealed class AppGoalService(
    IIntegrationCredentialStore credentials,
    IUserMcpServerRegistry servers,
    ICalendarFeed calendar,
    IOwnerSettingsStore settings) : IAppGoalService
{
    public async Task<AppGoalSearch> SearchAsync(Guid ownerId, string? query, CancellationToken cancellationToken)
    {
        var matched = AppGoalCatalog.Match(query);
        if (matched.Count == 0)
            return new AppGoalSearch([], true, AppGoalCatalog.BuildSuggestion);
        var statuses = await IntegrationPackCatalog.ListAsync(credentials, servers, ownerId, cancellationToken);
        var pins = (await settings.GetAsync<AppPinState>(ownerId, AppPinState.Section, cancellationToken))?.Goals ?? [];
        var views = new List<AppGoalView>();
        foreach (var goal in matched)
        {
            var status = statuses.First(item => item.Pack.Id == goal.PackId);
            var ready = IsReady(status);
            views.Add(new AppGoalView(goal.Id, goal.Title, goal.Purpose, status.Pack.Id, status.Pack.Name,
                ready ? AppGoalStatuses.Ready : AppGoalStatuses.NotConnected,
                AppGoalCatalog.NextStep(status.Pack, ready), AppGoalCatalog.Preview(status.Pack),
                ready ? goal.Try : null, ready && pins.Contains(goal.Id)));
        }
        return new AppGoalSearch(views, false, null);
    }

    public async Task<AppTryResult?> TryAsync(Guid ownerId, string goalId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var goal = AppGoalCatalog.All.FirstOrDefault(item => item.Id == goalId);
        if (goal is null || !await IsReadyAsync(ownerId, goal, cancellationToken)) return null;
        if (goal.Try.Kind != AppTryKinds.CalendarToday) return new AppTryResult(goal.Try.Kind, [], goal.Try.Prompt);
        var start = now.UtcDateTime.Date;
        try
        {
            var events = await calendar.ListUpcomingAsync(ownerId, new DateTimeOffset(start, TimeSpan.Zero),
                new DateTimeOffset(start.AddDays(1), TimeSpan.Zero), cancellationToken);
            return new AppTryResult(goal.Try.Kind, events, null);
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException
                                              or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // A real, targeted next step instead of a fake empty day.
            return new AppTryResult(goal.Try.Kind, [], null,
                "Your calendar could not be read just now. Check that the link you saved still works, or save a new one.");
        }
    }

    public async Task<bool> PinAsync(Guid ownerId, string goalId, CancellationToken cancellationToken)
    {
        var goal = AppGoalCatalog.All.FirstOrDefault(item => item.Id == goalId);
        if (goal is null || !await IsReadyAsync(ownerId, goal, cancellationToken)) return false;
        var pins = (await settings.GetAsync<AppPinState>(ownerId, AppPinState.Section, cancellationToken))?.Goals ?? [];
        if (pins.Contains(goalId)) return true;
        if (pins.Count >= AppPinState.Max) return false;
        await settings.SaveAsync(ownerId, AppPinState.Section, new AppPinState([.. pins, goalId]), cancellationToken);
        return true;
    }

    public async Task<bool> UnpinAsync(Guid ownerId, string goalId, CancellationToken cancellationToken)
    {
        var pins = (await settings.GetAsync<AppPinState>(ownerId, AppPinState.Section, cancellationToken))?.Goals ?? [];
        if (!pins.Contains(goalId)) return false;
        await settings.SaveAsync(ownerId, AppPinState.Section,
            new AppPinState(pins.Where(id => id != goalId).ToArray()), cancellationToken);
        return true;
    }

    private async Task<bool> IsReadyAsync(Guid ownerId, AppGoal goal, CancellationToken cancellationToken)
    {
        var statuses = await IntegrationPackCatalog.ListAsync(credentials, servers, ownerId, cancellationToken);
        return IsReady(statuses.First(item => item.Pack.Id == goal.PackId));
    }

    /// <summary>
    /// A calendar is ready with a saved link or an MCP server; mail and contacts only with an MCP server, since
    /// a stored token alone cannot read anything.
    /// </summary>
    private static bool IsReady(IntegrationPackStatus status) =>
        status.McpServerId is not null || (status.Pack.SupportsIcs && status.HasIcs);
}
