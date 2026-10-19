using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Setup;

public static class CapabilityStatuses
{
    public const string Ready = "ready";
    public const string NotConnected = "not_connected";
    public const string NeedsReauth = "needs_reauth";
    public const string Unavailable = "unavailable";
    public const string Unsupported = "unsupported";
}

public static class SetupGoalIds
{
    public const string Talk = "talk";
    public const string Reminder = "reminder";
    public const string Calendar = "calendar";
    public const string Source = "source";
    public const string Brief = "brief";

    public static readonly string[] All = [Talk, Reminder, Calendar, Source, Brief];

    /// <summary>Goals that need an outside app, so declining them is a normal choice.</summary>
    public static readonly string[] Optional = [Calendar, Source, Brief];
}

/// <summary>What was found when the owner's setup was looked at. Nothing is probed by sending or creating anything.</summary>
public sealed record SetupFacts(
    bool ModelSignedIn,
    string? ModelProblem,
    bool HasConversation,
    bool HasReminder,
    bool CalendarConnected,
    bool HasLibraryItem,
    bool BriefEnabled,
    string? BriefTime,
    SetupEnvironment? Environment = null,
    int PushDevices = 0,
    int AppsTotal = 0,
    int AppsNeedingAttention = 0);

/// <summary>
/// What the server could check without sending or creating anything an owner would notice: whether push delivery is set
/// up, and whether the upload scanner and file storage answer. Null means it was not checked.
/// </summary>
public sealed record SetupEnvironment(bool PushConfigured, bool? ScannerReachable, bool? StorageReachable);

/// <summary>One thing Jarvis depends on, whether it can be used now, and the page that fixes it.</summary>
public sealed record Capability(string Id, string Title, string Status, string Detail, string? Destination);

/// <summary>One thing the owner wants to get done, in plain words.</summary>
public sealed record SetupGoal(
    string Id,
    string Title,
    string Why,
    bool Done,
    bool Declined,
    string Action,
    string? Destination,
    string? Blocker);

public sealed record SetupView(
    IReadOnlyList<SetupGoal> Goals,
    IReadOnlyList<Capability> Capabilities,
    int Done,
    int Total,
    string? NextGoalId,
    string Summary);

/// <summary>The owner's choices only (owner settings section <see cref="Section"/>): goals they declined.</summary>
public sealed record SetupJourney(IReadOnlyList<string> Declined)
{
    public const string Section = "setup.journey";
}

public sealed record SupportBundle(
    string GeneratedAt,
    string Version,
    IReadOnlyList<Capability> Capabilities,
    IReadOnlyList<string> Goals,
    string Note);

public static class SetupRules
{
    public static IReadOnlyList<Capability> Capabilities(SetupFacts facts) =>
        facts.Environment is null ? Base(facts) : Base(facts).Where(c => c.Id != "push")
            .Concat(Environmental(facts, facts.Environment)).ToArray();

    private static IReadOnlyList<Capability> Environmental(SetupFacts facts, SetupEnvironment environment) =>
    [
        new("apps", "Connected apps",
            facts.AppsTotal == 0 ? CapabilityStatuses.NotConnected
                : facts.AppsNeedingAttention > 0 ? CapabilityStatuses.Unavailable : CapabilityStatuses.Ready,
            facts.AppsTotal == 0 ? "No apps connected. That is fine; connect one only when you want it."
                : facts.AppsNeedingAttention > 0
                    ? $"{facts.AppsNeedingAttention} of {facts.AppsTotal} apps need attention. Open Find an app to fix them."
                    : $"{facts.AppsTotal} app{(facts.AppsTotal == 1 ? "" : "s")} connected.",
            facts.AppsNeedingAttention > 0 || facts.AppsTotal == 0 ? "find-app" : null),
        new("push", "Notifications on this phone",
            !environment.PushConfigured ? CapabilityStatuses.Unavailable
                : facts.PushDevices == 0 ? CapabilityStatuses.NotConnected : CapabilityStatuses.Ready,
            !environment.PushConfigured
                ? "Push delivery is not set up on this server, so notifications only appear in the app."
                : facts.PushDevices == 0
                    ? "No phone is registered for push yet. Allow notifications on your phone."
                    : $"{facts.PushDevices} phone{(facts.PushDevices == 1 ? "" : "s")} registered. Whether a phone shows a push is not something Jarvis can see.",
            environment.PushConfigured && facts.PushDevices > 0 ? null : "notifications"),
        Probe("scanner", "Upload scanning", environment.ScannerReachable,
            "Uploads are scanned before they are kept.", "The upload scanner is not reachable, so uploads are refused until it is."),
        Probe("storage", "File storage", environment.StorageReachable,
            "Files can be stored and read.", "File storage is not reachable, so uploads and downloads will fail until it is.")
    ];

    private static Capability Probe(string id, string title, bool? reachable, string ready, string down) =>
        new(id, title, reachable switch { true => CapabilityStatuses.Ready, false => CapabilityStatuses.Unavailable, _ => CapabilityStatuses.Unsupported },
            reachable switch { true => ready, false => down, _ => "Jarvis did not check this." }, null);

    private static IReadOnlyList<Capability> Base(SetupFacts facts) =>
    [
        new("model", "Jarvis's brain",
            facts.ModelSignedIn ? CapabilityStatuses.Ready
                : facts.ModelProblem is not null ? CapabilityStatuses.NeedsReauth : CapabilityStatuses.NotConnected,
            facts.ModelSignedIn ? "Signed in, so Jarvis can answer."
                : facts.ModelProblem is not null
                    ? "The sign-in needs to be done again. Open Models to sign in."
                    : "Jarvis is not signed in to a model yet. Open Models to sign in.",
            facts.ModelSignedIn ? null : "models"),
        new("calendar", "Calendar",
            facts.CalendarConnected ? CapabilityStatuses.Ready : CapabilityStatuses.NotConnected,
            facts.CalendarConnected
                ? "Connected. Jarvis can read your events."
                : "Not connected. That is fine; connect it only if you want help with your agenda.",
            facts.CalendarConnected ? null : "find-app"),
        new("brief", "Morning brief",
            facts.BriefEnabled ? CapabilityStatuses.Ready : CapabilityStatuses.NotConnected,
            facts.BriefEnabled ? $"Arrives at {facts.BriefTime}." : "Off. Choose a time if you want one.",
            facts.BriefEnabled ? null : "briefing"),
        new("push", "Notifications on this phone", CapabilityStatuses.Unsupported,
            "Jarvis cannot check from here whether this phone is registered. Open Notifications to see.",
            "notifications")
    ];

    public static SetupView Build(SetupFacts facts, IReadOnlyCollection<string> declined)
    {
        var capabilities = Capabilities(facts);
        var model = capabilities[0];
        var goals = new List<SetupGoal>
        {
            Goal(SetupGoalIds.Talk, "Talk to Jarvis", "Ask anything; Jarvis learns what you care about.",
                facts.HasConversation, declined, "Start a chat", null,
                model.Status == CapabilityStatuses.Ready ? null : model.Detail),
            Goal(SetupGoalIds.Reminder, "Set a reminder", "Say what and when; Jarvis tells you at that time.",
                facts.HasReminder, declined, "Make a reminder", "reminders",
                model.Status == CapabilityStatuses.Ready ? null : "Setting one in chat needs Jarvis to be signed in. You can also add it in Reminders."),
            Goal(SetupGoalIds.Calendar, "Connect your calendar", "See what is on today and plan around it.",
                facts.CalendarConnected, declined, "Connect", "find-app", null),
            Goal(SetupGoalIds.Source, "Add something to read", "Save a page or note so Jarvis can answer from it.",
                facts.HasLibraryItem, declined, "Open the Library", "library", null),
            Goal(SetupGoalIds.Brief, "Choose when you get a brief", "A short morning summary of the day.",
                facts.BriefEnabled, declined, "Choose a time", "briefing", null)
        };
        var done = goals.Count(g => g.Done);
        var next = goals.FirstOrDefault(g => !g.Done && !g.Declined);
        var summary = next is null
            ? "You are set up. Anything you skipped can be turned on later."
            : $"{done} of {goals.Count} done. Next: {next.Title.ToLowerInvariant()}.";
        return new SetupView(goals, capabilities, done, goals.Count, next?.Id, summary);
    }

    private static SetupGoal Goal(string id, string title, string why, bool done, IReadOnlyCollection<string> declined,
        string action, string? destination, string? blocker) =>
        new(id, title, why, done, !done && declined.Contains(id), action, destination, blocker);

    public static SupportBundle Bundle(SetupView view, string version, DateTimeOffset now) =>
        new(now.ToString("yyyy-MM-dd HH:mm 'UTC'"), version, view.Capabilities,
            view.Goals.Select(g => $"{g.Id}: {(g.Done ? "done" : g.Declined ? "skipped" : "open")}").ToArray(),
            "Contains versions and on/off states only: no names, messages, hostnames, tokens or addresses.");
}

public sealed record SetupOperation(bool Succeeded, string? Field = null, string? Message = null)
{
    public static SetupOperation Ok() => new(true);
    public static SetupOperation Invalid(string field, string message) => new(false, field, message);
}

public interface ISetupService
{
    Task<SetupView> GetAsync(Guid ownerId, bool modelSignedIn, string? modelProblem, CancellationToken cancellationToken,
        SetupEnvironment? environment = null);
    Task<SetupOperation> DeclineAsync(Guid ownerId, string goalId, CancellationToken cancellationToken);
    Task<SetupOperation> ReopenAsync(Guid ownerId, string goalId, CancellationToken cancellationToken);
}

public sealed class SetupService(
    Conversations.IConversationStore conversations,
    Workflows.IReminderService reminders,
    Workflows.IDailyBriefingService briefings,
    Library.ILibraryService library,
    IIntegrationCredentialStore credentials,
    IUserMcpServerRegistry servers,
    IOwnerSettingsStore settings,
    Workflows.IPushDeviceRepository? pushDevices = null) : ISetupService
{
    public async Task<SetupView> GetAsync(Guid ownerId, bool modelSignedIn, string? modelProblem,
        CancellationToken cancellationToken, SetupEnvironment? environment = null)
    {
        var packs = await IntegrationPackCatalog.ListAsync(credentials, servers, ownerId, cancellationToken);
        var calendar = packs.FirstOrDefault(p => p.Pack.Id == IntegrationPackIds.Calendar);
        var brief = await briefings.GetAsync(ownerId, cancellationToken);
        var facts = new SetupFacts(modelSignedIn, modelProblem,
            (await conversations.ListAsync(ownerId, cancellationToken)).Count > 0,
            (await reminders.ListAsync(ownerId, cancellationToken)).Count > 0,
            calendar is { } c && (c.McpServerId is not null || c.Pack.SupportsIcs && c.HasIcs),
            (await library.ListAsync(ownerId, new Library.LibraryQuery(null, null, null, 1), cancellationToken)).Count > 0,
            brief is { Enabled: true }, brief?.LocalTime.ToString("HH:mm"));
        if (environment is not null)
        {
            var installed = await servers.ListAsync(ownerId, cancellationToken);
            facts = facts with
            {
                Environment = environment,
                PushDevices = pushDevices is null ? 0 : await pushDevices.CountAsync(ownerId, cancellationToken),
                AppsTotal = installed.Count,
                AppsNeedingAttention = installed.Count(server => server is { Enabled: true, IsValid: false } ||
                                                                 server.Secrets?.Any(secret => secret is { Required: true, IsSet: false }) == true)
            };
        }
        var journey = await settings.GetAsync<SetupJourney>(ownerId, SetupJourney.Section, cancellationToken);
        return SetupRules.Build(facts, journey?.Declined ?? []);
    }

    public Task<SetupOperation> DeclineAsync(Guid ownerId, string goalId, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, goalId, add: true, cancellationToken);

    public Task<SetupOperation> ReopenAsync(Guid ownerId, string goalId, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, goalId, add: false, cancellationToken);

    private async Task<SetupOperation> ChangeAsync(Guid ownerId, string goalId, bool add,
        CancellationToken cancellationToken)
    {
        if (!SetupGoalIds.All.Contains(goalId))
            return SetupOperation.Invalid("goalId", "That is not a setup step.");
        if (add && !SetupGoalIds.Optional.Contains(goalId))
            return SetupOperation.Invalid("goalId", "That step is how Jarvis works; it cannot be skipped.");
        var current = (await settings.GetAsync<SetupJourney>(ownerId, SetupJourney.Section, cancellationToken))
            ?.Declined.ToList() ?? [];
        if (add && !current.Contains(goalId)) current.Add(goalId);
        if (!add) current.Remove(goalId);
        await settings.SaveAsync(ownerId, SetupJourney.Section, new SetupJourney(current), cancellationToken);
        return SetupOperation.Ok();
    }
}
