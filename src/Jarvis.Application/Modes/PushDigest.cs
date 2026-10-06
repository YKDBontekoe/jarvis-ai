using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Modes;

/// <summary>A push a mode kept off the phone. Only what the digest needs: no body text.</summary>
public sealed record HeldPush(Guid NotificationId, string Type, string Title, DateTimeOffset At);

/// <summary>Pushes held back by a mode such as Sleep or Meeting, waiting to be summed up once pushes are allowed again.</summary>
public sealed record PushDigestState(IReadOnlyList<HeldPush>? Held = null)
{
    public IReadOnlyList<HeldPush> Items => Held ?? [];
}

/// <summary>
/// A mode that keeps pushes off the phone used to drop them. They now wait here, and one "while you were away"
/// notification replaces them when the mode lets pushes through again. The notifications themselves stay in the app.
/// </summary>
public interface IPushDigestService
{
    /// <summary>Keeps a suppressed push for the digest. Returns false when the owner switched the digest off.</summary>
    Task<bool> HoldAsync(Guid ownerId, Guid notificationId, string type, string title,
        CancellationToken cancellationToken);

    /// <summary>Sends the digest when something is held and pushes are allowed again. True when one was sent.</summary>
    Task<bool> FlushIfDueAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListOwnersAsync(CancellationToken cancellationToken);
}

public sealed class PushDigestService(
    IOwnerSettingsStore settings,
    IModeService modes,
    INotificationRepository notifications,
    TimeProvider? timeProvider = null) : IPushDigestService
{
    public const string DigestType = "digest.while_away";
    public const int MaxHeld = 50;
    public const int MaxLines = 6;

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<bool> HoldAsync(Guid ownerId, Guid notificationId, string type, string title,
        CancellationToken cancellationToken)
    {
        // The digest is itself a push; holding it would summarise the summary forever.
        if (type == DigestType) return false;
        var autonomy = await settings.GetAsync<AutonomySettings>(ownerId, SettingsSections.Autonomy, cancellationToken)
                       ?? AutonomySettings.Default;
        if (!autonomy.Enabled || !autonomy.DigestInsteadOfDrop) return false;

        var state = await settings.GetAsync<PushDigestState>(ownerId, SettingsSections.PushDigest, cancellationToken)
                    ?? new PushDigestState();
        if (state.Items.Any(item => item.NotificationId == notificationId)) return true;
        var held = state.Items.Append(new HeldPush(notificationId, type, title, _clock.GetUtcNow()))
            .TakeLast(MaxHeld).ToArray();
        await settings.SaveAsync(ownerId, SettingsSections.PushDigest, new PushDigestState(held), cancellationToken);
        return true;
    }

    public async Task<bool> FlushIfDueAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<PushDigestState>(ownerId, SettingsSections.PushDigest, cancellationToken);
        if (state is null || state.Items.Count == 0) return false;
        if (!await modes.ShouldPushAsync(ownerId, DigestType, cancellationToken)) return false;

        await notifications.CreateAsync(ownerId, DigestType, "While you were away", Compose(state.Items), null,
            cancellationToken);
        // Cleared after the notification exists: a crash in between repeats the digest rather than losing it.
        await settings.SaveAsync(ownerId, SettingsSections.PushDigest, new PushDigestState(), cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<Guid>> ListOwnersAsync(CancellationToken cancellationToken) =>
        settings.ListOwnersAsync(SettingsSections.PushDigest, cancellationToken);

    public static string Compose(IReadOnlyList<HeldPush> held)
    {
        var lines = held.OrderBy(item => item.At)
            .Select(item => item.Title.Trim())
            .Where(title => title.Length > 0)
            .GroupBy(title => title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count() == 1 ? "• " + group.Key : $"• {group.Key} ×{group.Count()}")
            .ToArray();
        var header = held.Count == 1 ? "1 notification came in:" : $"{held.Count} notifications came in:";
        var shown = lines.Take(MaxLines).Prepend(header);
        var more = lines.Length - MaxLines;
        return string.Join('\n', more > 0 ? shown.Append($"…and {more} more") : shown);
    }
}
