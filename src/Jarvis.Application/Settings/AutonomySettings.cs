namespace Jarvis.Application.Settings;

/// <summary>
/// Owner-chosen approval posture. <c>ask</c> (default) gates every sensitive tool. <c>trusted</c> lets Jarvis
/// call the MCP tools the owner already allowlisted, read MCP resources, and run automations without a prompt.
/// Tools that run code, delete data, or change MCP registration always require approval.
/// </summary>
public sealed record AutonomySettings(string Mode = AutonomySettings.Ask)
{
    public const string Ask = "ask";
    public const string Trusted = "trusted";

    public static AutonomySettings Default { get; } = new();

    public bool IsTrusted => Mode == Trusted;

    public AutonomySettings Normalize() => (Mode?.Trim().ToLowerInvariant()) switch
    {
        Ask => this with { Mode = Ask },
        Trusted => this with { Mode = Trusted },
        _ => throw new ArgumentException("Autonomy mode must be 'ask' or 'trusted'.")
    };
}
