namespace Jarvis.Application.Settings;

/// <summary>Per-owner JSON settings grouped by feature section.</summary>
public interface IOwnerSettingsStore
{
    Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken cancellationToken) where T : class;
    Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken cancellationToken) where T : class;
    Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken cancellationToken);
}

public static class SettingsSections
{
    public const string Models = "models";
    public const string Learning = "learning";
    public const string Persona = "persona";
    public const string Devices = "devices";
}
