namespace Jarvis.Application.Settings;

/// <summary>Per-owner JSON settings grouped by feature section.</summary>
public interface IOwnerSettingsStore
{
    Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken cancellationToken) where T : class;
    Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken cancellationToken) where T : class;
    Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken cancellationToken);
}

/// <summary>
/// Every owner on this install. Features that are on by default for owners who never saved a settings row use this
/// instead of <see cref="IOwnerSettingsStore.ListOwnersAsync"/>, which only lists owners with a stored row.
/// </summary>
public interface IOwnerDirectory
{
    Task<IReadOnlyList<Guid>> ListOwnersAsync(CancellationToken cancellationToken);
}

public static class SettingsSections
{
    public const string Models = "models";
    public const string Learning = "learning";
    public const string Persona = "persona";
    public const string Devices = "devices";
    public const string Voice = "voice";
    public const string WeeklyReview = "weekly-review";
    public const string StandingApprovals = "standing-approvals";
    public const string Modes = "context-modes";
    public const string Routines = "routines";
    public const string PeopleRadar = "people-radar";
}
