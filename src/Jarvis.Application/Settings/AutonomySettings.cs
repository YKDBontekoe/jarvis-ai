namespace Jarvis.Application.Settings;

/// <summary>How far Jarvis may go on its own, as chosen by the owner.</summary>
public static class AutonomyLevels
{
    /// <summary>Read-only tools run without asking, and so do reversible changes inside background tasks.</summary>
    public const string Full = "full";

    /// <summary>Read-only tools run without asking; everything that changes something still asks.</summary>
    public const string Standard = "standard";

    /// <summary>Nothing is approved automatically except what the owner granted per category.</summary>
    public const string AskEverything = "ask_everything";

    /// <summary>
    /// Opt-in: on top of <see cref="Full"/>, reversible changes run without asking in chat too, and background runs
    /// (tasks, and Jarvis's reactions to events) may send or act outside Jarvis in the categories the owner allowed in
    /// <see cref="AutonomySettings.AutonomousOutboundCategories"/>. Deleting, reading private data and anything
    /// unclassified still always asks.
    /// </summary>
    public const string Autonomous = "autonomous";

    public static bool IsValid(string? level) => level is Full or Standard or AskEverything or Autonomous;
}

/// <summary>
/// The approval categories that may ever be allowed to reach outside Jarvis unattended. Changing integrations,
/// coding Jarvis itself and anything else stays out on purpose.
/// </summary>
public static class AutonomousOutboundCategories
{
    public static readonly IReadOnlyList<(string Key, string Label)> Eligible =
    [
        ("whatsapp.send", "Send WhatsApp messages"),
        ("automations.channel_message", "Send messages for automations"),
        ("browser", "Use the browser"),
        ("mcp.invoke", "Use integration tools"),
        ("devices.open_url", "Open links on your devices"),
        ("agents.delegate", "Ask other agents")
    ];

    public static bool IsEligible(string? key) => key is not null && Eligible.Any(x => x.Key == key);

    /// <summary>True when <paramref name="categoryKey"/> is an allowed category or sits under one (mcp.invoke.server.tool).</summary>
    public static bool Allows(IReadOnlyCollection<string>? allowed, string? categoryKey) =>
        allowed is { Count: > 0 } && !string.IsNullOrEmpty(categoryKey) &&
        allowed.Any(key => IsEligible(key) &&
                           (categoryKey == key || categoryKey.StartsWith(key + ".", StringComparison.Ordinal)));
}

/// <summary>
/// How much Jarvis may do on its own between conversations. Stored in the owner settings section
/// <c>autonomy</c>; a missing row means these defaults, and <see cref="Enabled"/> switches every autonomous action off
/// at once. Autonomous work stays inside the normal approval rules: anything that sends, spends, deletes or
/// otherwise reaches outside Jarvis still waits for the owner.
/// </summary>
public sealed record AutonomySettings(
    bool Enabled = true,
    bool HeartbeatMayStartTasks = true,
    int MaxHeartbeatTasksPerDay = 3,
    int MaxHeartbeatTasksPerRun = 1,
    bool TriageInbox = true,
    bool DigestInsteadOfDrop = true,
    string Level = AutonomyLevels.Full,
    bool AutoApproveReadOnly = true,
    bool AutoApproveMcpReadHints = true,
    int MaxAutoApprovalsPerDay = 200,
    bool ReactToEvents = true,
    int MaxReactionsPerDay = 20,
    IReadOnlyList<string>? AutonomousOutboundCategories = null,
    int MaxAutonomousOutboundPerDay = 10)
{
    public const int MaxTasksPerDayLimit = 10;
    public const int MaxTasksPerRunLimit = 3;
    public const int MaxAutoApprovalsLimit = 2_000;
    public const int MaxReactionsLimit = 100;
    public const int MaxAutonomousOutboundLimit = 50;

    public static AutonomySettings Default { get; } = new();

    public AutonomySettings Normalize()
    {
        if (MaxHeartbeatTasksPerDay is < 0 or > MaxTasksPerDayLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxTasksPerDayLimit} background tasks per day.", nameof(MaxHeartbeatTasksPerDay));
        if (MaxHeartbeatTasksPerRun is < 0 or > MaxTasksPerRunLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxTasksPerRunLimit} background tasks per check.", nameof(MaxHeartbeatTasksPerRun));
        if (!AutonomyLevels.IsValid(Level))
            throw new ArgumentException("Choose full, standard or ask_everything.", nameof(Level));
        if (MaxAutoApprovalsPerDay is < 0 or > MaxAutoApprovalsLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxAutoApprovalsLimit} automatic approvals per day.",
                nameof(MaxAutoApprovalsPerDay));
        if (MaxReactionsPerDay is < 0 or > MaxReactionsLimit)
            throw new ArgumentException($"Choose between 0 and {MaxReactionsLimit} reactions per day.",
                nameof(MaxReactionsPerDay));
        if (MaxAutonomousOutboundPerDay is < 0 or > MaxAutonomousOutboundLimit)
            throw new ArgumentException(
                $"Choose between 0 and {MaxAutonomousOutboundLimit} unattended outside actions per day.",
                nameof(MaxAutonomousOutboundPerDay));
        var categories = (AutonomousOutboundCategories ?? []).Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (categories.FirstOrDefault(x => !Settings.AutonomousOutboundCategories.IsEligible(x)) is { } unknown)
            throw new ArgumentException($"'{unknown}' cannot run unattended.", nameof(AutonomousOutboundCategories));
        return this with { AutonomousOutboundCategories = categories };
    }

    /// <summary>True when Jarvis may react to events on its own at all.</summary>
    public bool CanReact => Enabled && ReactToEvents && MaxReactionsPerDay > 0 && Level != AutonomyLevels.AskEverything;

    /// <summary>True when the heartbeat may start background tasks at all.</summary>
    public bool CanStartHeartbeatTasks =>
        Enabled && HeartbeatMayStartTasks && MaxHeartbeatTasksPerDay > 0 && MaxHeartbeatTasksPerRun > 0;
}
