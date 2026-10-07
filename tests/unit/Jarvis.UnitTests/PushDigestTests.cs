using Jarvis.Application.Modes;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PushDigestTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000d16e");
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Held_pushes_wait_while_the_mode_blocks_and_become_one_digest_afterwards()
    {
        var world = new World { PushAllowed = false };
        var digest = world.Create();

        Assert.True(await digest.HoldAsync(Owner, Guid.NewGuid(), "reminder.due", "Take medication", default));
        Assert.True(await digest.HoldAsync(Owner, Guid.NewGuid(), "watch.triggered", "Condition met", default));
        Assert.False(await digest.FlushIfDueAsync(Owner, default));
        Assert.Empty(world.Sent);

        world.PushAllowed = true;
        Assert.True(await digest.FlushIfDueAsync(Owner, default));

        var sent = Assert.Single(world.Sent);
        Assert.Equal(PushDigestService.DigestType, sent.Type);
        Assert.Equal("While you were away", sent.Title);
        Assert.Equal("2 notifications came in:\n• Take medication\n• Condition met", sent.Body);
        Assert.False(await digest.FlushIfDueAsync(Owner, default));
        Assert.Single(world.Sent);
    }

    [Fact]
    public async Task The_same_notification_is_held_once_even_with_several_devices()
    {
        var world = new World();
        var digest = world.Create();
        var id = Guid.NewGuid();

        await digest.HoldAsync(Owner, id, "reminder.due", "Call mum", default);
        await digest.HoldAsync(Owner, id, "reminder.due", "Call mum", default);

        var state = await world.Settings.GetAsync<PushDigestState>(Owner, SettingsSections.PushDigest, default);
        Assert.Single(state!.Items);
    }

    [Fact]
    public async Task Switching_the_digest_off_or_autonomy_off_keeps_the_old_drop_behaviour()
    {
        var world = new World();
        var digest = world.Create();
        await world.Settings.SaveAsync(Owner, SettingsSections.Autonomy,
            new AutonomySettings(DigestInsteadOfDrop: false), default);
        Assert.False(await digest.HoldAsync(Owner, Guid.NewGuid(), "reminder.due", "Call mum", default));

        await world.Settings.SaveAsync(Owner, SettingsSections.Autonomy, new AutonomySettings(Enabled: false), default);
        Assert.False(await digest.HoldAsync(Owner, Guid.NewGuid(), "reminder.due", "Call mum", default));

        Assert.Null(await world.Settings.GetAsync<PushDigestState>(Owner, SettingsSections.PushDigest, default));
    }

    [Fact]
    public async Task The_digest_is_never_held_itself()
    {
        var world = new World();

        Assert.False(await world.Create().HoldAsync(Owner, Guid.NewGuid(), PushDigestService.DigestType,
            "While you were away", default));
    }

    [Fact]
    public async Task Only_the_newest_fifty_are_kept()
    {
        var world = new World();
        var digest = world.Create();
        for (var i = 0; i < PushDigestService.MaxHeld + 5; i++)
            await digest.HoldAsync(Owner, Guid.NewGuid(), "reminder.due", "Item " + i, default);

        var state = await world.Settings.GetAsync<PushDigestState>(Owner, SettingsSections.PushDigest, default);

        Assert.Equal(PushDigestService.MaxHeld, state!.Items.Count);
        Assert.Equal("Item 5", state.Items[0].Title);
    }

    [Fact]
    public void Digest_text_groups_repeats_and_caps_the_lines()
    {
        HeldPush Held(string title, int minute) =>
            new(Guid.NewGuid(), "reminder.due", title, Now.AddMinutes(minute));

        var text = PushDigestService.Compose(
        [
            Held("Stretch", 1), Held("Stretch", 2), Held("A", 3), Held("B", 4), Held("C", 5), Held("D", 6),
            Held("E", 7), Held("F", 8)
        ]);

        var lines = text.Split('\n');
        Assert.Equal("8 notifications came in:", lines[0]);
        Assert.Equal("• Stretch ×2", lines[1]);
        Assert.Equal(PushDigestService.MaxLines + 2, lines.Length);
        Assert.Equal("…and 1 more", lines[^1]);
        Assert.Equal("1 notification came in:\n• Solo", PushDigestService.Compose([Held("Solo", 0)]));
    }

    private sealed class World
    {
        public InMemorySettingsStore Settings { get; } = new();
        public bool PushAllowed { get; set; } = true;
        public List<(string Type, string Title, string Body)> Sent { get; } = [];

        public PushDigestService Create() => new(
            Settings,
            Fake<IModeService>.Create(("ShouldPushAsync", _ => PushAllowed)),
            Fake<INotificationRepository>.Create(("CreateAsync", args =>
            {
                Sent.Add(((string)args[1]!, (string)args[2]!, (string)args[3]!));
                return new NotificationRecord(Guid.NewGuid(), (string)args[1]!, (string)args[2]!, (string)args[3]!,
                    null, Now, null);
            })),
            new FixedTime(Now));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
