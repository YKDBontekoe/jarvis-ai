using Jarvis.Application.Search;

namespace Jarvis.Application.Navigation;

/// <summary>The model chooses a workflow, never a URL or a resource id.</summary>
public sealed record NavigationIntent(string Kind, string? Destination = null, string? Query = null);
public sealed record NavigationAction(string Label, string Description, SearchRouteTarget Route);
public sealed record NavigationResponse(string Message, IReadOnlyList<NavigationAction> Actions, bool Understood = true);
public sealed record NavigationDestination(string Id, string Label, string Description);

public interface INavigationIntentInterpreter
{
    Task<NavigationIntent?> InterpretAsync(Guid ownerId, string request, CancellationToken cancellationToken);
}

public static class NavigationCatalog
{
    public static readonly IReadOnlyList<NavigationDestination> Destinations = Array.AsReadOnly<NavigationDestination>([
        new("today", "Plan my day", "Today's agenda, priorities and attention"),
        new("whatsapp", "WhatsApp", "Your selected WhatsApp conversations and account setup"),
        new("tasks", "Tasks", "Work Jarvis is doing and its progress"),
        new("reminders", "Reminders", "Scheduled reminders and notifications"),
        new("memory", "Memory", "What Jarvis remembers, including editing and forgetting"),
        new("people", "People", "Contacts, relationships and follow-ups"),
        new("journal", "Journal", "Your journal and mood history"),
        new("expenses", "Expenses", "Spending, receipts and expense history"),
        new("habits", "Habits", "Routines, check-ins and progress"),
        new("projects", "Projects", "Project conversations, files and instructions"),
        new("files", "Files", "Documents shared with Jarvis"),
        new("approvals", "Approvals", "Actions waiting for your decision"),
        new("integrations", "Connected apps", "Connect or manage external apps"),
        new("channels", "Messaging accounts", "Manage WhatsApp and Signal connections"),
        new("weekly-review", "Weekly review", "A look back at your week"),
        new("automations", "Automations", "Recurring rules and schedules"),
        new("skills", "Skills", "Routines Jarvis knows"),
        new("coding", "Coding runs", "Code changes to review")
    ]);

    public static NavigationAction Open(NavigationDestination destination) => new(destination.Label,
        destination.Description, new SearchRouteTarget("utility", new Dictionary<string, string>
        {
            ["destination"] = destination.Id
        }));
}
