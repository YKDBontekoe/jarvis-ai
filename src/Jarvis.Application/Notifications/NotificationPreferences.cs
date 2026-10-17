using Jarvis.Application.Modes;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Notifications;

public static class NotificationCategoryIds
{
    public const string Reminders = "reminders";
    public const string Approvals = "approvals";
    public const string Problems = "problems";
    public const string Briefings = "briefings";
    public const string Updates = "updates";
}

public static class PushSettings
{
    public const string On = "on";

    /// <summary>Never buzzes the phone. The notification still waits in the app.</summary>
    public const string Quiet = "quiet";

    public static bool IsValid(string? value) => value is On or Quiet;
}

/// <summary>A kind of notification the owner can reason about, instead of one switch per internal event.</summary>
public sealed record NotificationCategory(
    string Id,
    string Title,
    string Description,
    bool Locked,
    string RepresentativeType);

public static class NotificationCategories
{
    public static readonly IReadOnlyList<NotificationCategory> All =
    [
        new(NotificationCategoryIds.Approvals, "Needs your decision",
            "Jarvis waiting for you to approve something. These always buzz, because nothing happens until you answer.",
            true, "approval.required"),
        new(NotificationCategoryIds.Reminders, "Reminders",
            "Your reminders when they are due.", false, "reminder.due"),
        new(NotificationCategoryIds.Problems, "Something went wrong",
            "A reminder, task or mission that failed. These always buzz, and stay in the app either way.",
            true, "reminder.failed"),
        new(NotificationCategoryIds.Briefings, "Briefings",
            "Your morning brief and weekly review.", false, "briefing.daily"),
        new(NotificationCategoryIds.Updates, "Updates and ideas",
            "Finished tasks, things Jarvis learned, budget alerts and check-ins.", false, "task.completed")
    ];

    public static NotificationCategory Find(string id) => All.FirstOrDefault(c => c.Id == id) ??
        throw new ArgumentOutOfRangeException(nameof(id));

    public static bool IsKnown(string? id) => All.Any(c => c.Id == id);

    /// <summary>Which category a notification type belongs to. Unknown types count as updates.</summary>
    public static string For(string type)
    {
        if (type is "approval.required" or "automation.approval" or "task.approval_required")
            return NotificationCategoryIds.Approvals;
        if (type is "reminder.due" or "whatsapp.reminder") return NotificationCategoryIds.Reminders;
        if (type.EndsWith(".failed", StringComparison.Ordinal)) return NotificationCategoryIds.Problems;
        if (type.StartsWith("briefing.", StringComparison.Ordinal)) return NotificationCategoryIds.Briefings;
        return NotificationCategoryIds.Updates;
    }
}

/// <summary>Owner settings section <see cref="Section"/>: category → <see cref="PushSettings"/>. Missing means on.</summary>
public sealed record NotificationPreferences(IReadOnlyDictionary<string, string>? Push = null)
{
    public const string Section = "notifications.preferences";

    public string For(string category) =>
        Push is not null && Push.TryGetValue(category, out var value) && PushSettings.IsValid(value)
            ? value
            : PushSettings.On;
}

public static class PushOutcomes
{
    /// <summary>Goes to the phone now.</summary>
    public const string Push = "push";

    /// <summary>Kept off the phone by the current mode; one "while you were away" digest follows.</summary>
    public const string Held = "held";

    /// <summary>Chosen by the owner to stay in the app only.</summary>
    public const string AppOnly = "app_only";
}

public sealed record PushDecision(string Outcome, string Reason);

public static class NotificationPolicy
{
    /// <summary>
    /// The owner's choice comes first (a quiet category never buzzes, unless it is locked), then the current mode
    /// decides between pushing and holding for the digest.
    /// </summary>
    public static PushDecision Decide(string type, NotificationPreferences preferences, bool modeAllows,
        string modeLabel)
    {
        var category = NotificationCategories.Find(NotificationCategories.For(type));
        if (!category.Locked && preferences.For(category.Id) == PushSettings.Quiet)
            return new PushDecision(PushOutcomes.AppOnly,
                $"{category.Title} are set to stay in the app, so they do not buzz your phone.");
        if (!modeAllows)
            return new PushDecision(PushOutcomes.Held,
                $"{modeLabel} keeps this off your phone; it waits in the app and in the next \"while you were away\" summary.");
        return new PushDecision(PushOutcomes.Push, "It goes to your phone right away.");
    }
}

/// <summary>What happens to one category now and in each mode.</summary>
public sealed record CategoryView(
    string Id,
    string Title,
    string Description,
    bool Locked,
    string Setting,
    PushDecision Now,
    IReadOnlyList<ModeEffect> Modes);

public sealed record ModeEffect(string Mode, string Label, string Outcome);

public sealed record NotificationPreferencesView(
    string CurrentMode,
    string CurrentModeLabel,
    string Summary,
    IReadOnlyList<CategoryView> Categories);

public sealed record NotificationPreferenceOperation(bool Succeeded, NotificationPreferencesView? View = null,
    string? Field = null, string? Message = null);

public interface INotificationPreferenceService
{
    Task<NotificationPreferencesView> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<NotificationPreferenceOperation> SetAsync(Guid ownerId, string category, string? push,
        CancellationToken cancellationToken);

    /// <summary>What to do with a push that is about to be sent. Falls back to pushing if anything is unreadable.</summary>
    Task<PushDecision> DecideAsync(Guid ownerId, string type, CancellationToken cancellationToken);
}

public sealed class NotificationPreferenceService(IOwnerSettingsStore settings, IModeService modes)
    : INotificationPreferenceService
{
    public async Task<NotificationPreferencesView> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var preferences = await LoadAsync(ownerId, cancellationToken);
        var state = await modes.GetStateAsync(ownerId, cancellationToken);
        var label = state.Modes.FirstOrDefault(m => m.Definition.Id == state.Current.Mode)?.Definition.Label ??
                    state.Current.Mode;
        var categories = NotificationCategories.All.Select(category =>
        {
            var now = NotificationPolicy.Decide(category.RepresentativeType, preferences,
                NotificationLevels.Allows(state.Policy.Notifications, category.RepresentativeType), label);
            var perMode = state.Modes.Select(mode => new ModeEffect(mode.Definition.Id, mode.Definition.Label,
                NotificationPolicy.Decide(category.RepresentativeType, preferences,
                    NotificationLevels.Allows(mode.Definition.Policy.Notifications, category.RepresentativeType),
                    mode.Definition.Label).Outcome)).ToArray();
            return new CategoryView(category.Id, category.Title, category.Description, category.Locked,
                category.Locked ? PushSettings.On : preferences.For(category.Id), now, perMode);
        }).ToArray();
        var held = categories.Count(c => c.Now.Outcome == PushOutcomes.Held);
        var quiet = categories.Count(c => c.Now.Outcome == PushOutcomes.AppOnly);
        var summary = held > 0
            ? $"{label} is on: {held} kind{(held == 1 ? " of notification waits" : "s of notification wait")} for you in the app."
            : quiet > 0
                ? $"{quiet} kind{(quiet == 1 ? " of notification stays" : "s of notification stay")} in the app only, by your choice."
                : "Everything reaches your phone right away.";
        return new NotificationPreferencesView(state.Current.Mode, label, summary, categories);
    }

    public async Task<NotificationPreferenceOperation> SetAsync(Guid ownerId, string category, string? push,
        CancellationToken cancellationToken)
    {
        if (!NotificationCategories.IsKnown(category))
            return new NotificationPreferenceOperation(false, null, "category", "That is not a kind of notification.");
        if (!PushSettings.IsValid(push))
            return new NotificationPreferenceOperation(false, null, "push", "Choose on or quiet.");
        if (NotificationCategories.Find(category).Locked && push == PushSettings.Quiet)
            return new NotificationPreferenceOperation(false, null, "push",
                "This kind always buzzes, so it cannot be quieted. It stays in the app either way.");
        var current = await LoadAsync(ownerId, cancellationToken);
        var next = new Dictionary<string, string>(current.Push ?? new Dictionary<string, string>())
        {
            [category] = push!
        };
        await settings.SaveAsync(ownerId, NotificationPreferences.Section, new NotificationPreferences(next),
            cancellationToken);
        return new NotificationPreferenceOperation(true, await GetAsync(ownerId, cancellationToken));
    }

    public async Task<PushDecision> DecideAsync(Guid ownerId, string type, CancellationToken cancellationToken)
    {
        try
        {
            var preferences = await LoadAsync(ownerId, cancellationToken);
            var state = await modes.GetStateAsync(ownerId, cancellationToken);
            var label = state.Modes.FirstOrDefault(m => m.Definition.Id == state.Current.Mode)?.Definition.Label ??
                        state.Current.Mode;
            return NotificationPolicy.Decide(type, preferences,
                NotificationLevels.Allows(state.Policy.Notifications, type), label);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broken settings row must never swallow a notification.
            return new PushDecision(PushOutcomes.Push, "It goes to your phone right away.");
        }
    }

    private async Task<NotificationPreferences> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<NotificationPreferences>(ownerId, NotificationPreferences.Section, cancellationToken)
        ?? new NotificationPreferences();
}
