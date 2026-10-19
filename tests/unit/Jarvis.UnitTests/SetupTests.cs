using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Library;
using Jarvis.Application.Setup;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SetupTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fd01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fd02");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static SetupFacts Facts(bool model = true, string? problem = null, bool chat = false, bool reminder = false,
        bool calendar = false, bool library = false, bool brief = false) =>
        new(model, problem, chat, reminder, calendar, library, brief, brief ? "08:00" : null);

    [Fact]
    public void A_brand_new_owner_sees_five_open_steps_and_the_first_one_is_next()
    {
        var view = SetupRules.Build(Facts(), []);

        Assert.Equal(["talk", "reminder", "calendar", "source", "brief"], view.Goals.Select(g => g.Id));
        Assert.Equal(0, view.Done);
        Assert.Equal("talk", view.NextGoalId);
        Assert.Equal("0 of 5 done. Next: talk to jarvis.", view.Summary);
    }

    [Fact]
    public void Completed_steps_come_from_what_exists_and_the_next_one_moves_on()
    {
        var view = SetupRules.Build(Facts(chat: true, reminder: true), []);

        Assert.Equal(2, view.Done);
        Assert.Equal("calendar", view.NextGoalId);
        Assert.True(view.Goals.Single(g => g.Id == "reminder").Done);
    }

    [Fact]
    public void A_skipped_step_is_a_choice_not_a_nag_and_a_done_step_is_never_shown_as_skipped()
    {
        var view = SetupRules.Build(Facts(chat: true, reminder: true, calendar: true), ["source", "brief", "calendar"]);

        Assert.Equal("You are set up. Anything you skipped can be turned on later.", view.Summary);
        Assert.Null(view.NextGoalId);
        Assert.True(view.Goals.Single(g => g.Id == "source").Declined);
        Assert.False(view.Goals.Single(g => g.Id == "calendar").Declined);
    }

    [Fact]
    public void An_unconnected_optional_app_is_explained_without_making_the_app_look_broken()
    {
        var view = SetupRules.Build(Facts(chat: true, reminder: true), []);

        var calendar = view.Capabilities.Single(c => c.Id == "calendar");
        Assert.Equal(CapabilityStatuses.NotConnected, calendar.Status);
        Assert.Contains("That is fine", calendar.Detail);
        Assert.Equal("find-app", calendar.Destination);
        Assert.Equal(CapabilityStatuses.Ready, view.Capabilities.Single(c => c.Id == "model").Status);
        Assert.All(view.Goals, g => Assert.Null(g.Blocker));
    }

    [Fact]
    public void Model_trouble_is_actionable_and_only_blocks_the_steps_that_need_it()
    {
        var signedOut = SetupRules.Build(Facts(model: false), []);
        var expired = SetupRules.Build(Facts(model: false, problem: "sign-in"), []);

        var model = signedOut.Capabilities.Single(c => c.Id == "model");
        Assert.Equal(CapabilityStatuses.NotConnected, model.Status);
        Assert.Equal("models", model.Destination);
        Assert.Equal(CapabilityStatuses.NeedsReauth, expired.Capabilities.Single(c => c.Id == "model").Status);
        Assert.NotNull(signedOut.Goals.Single(g => g.Id == "talk").Blocker);
        Assert.Contains("Reminders", signedOut.Goals.Single(g => g.Id == "reminder").Blocker);
        Assert.Null(signedOut.Goals.Single(g => g.Id == "source").Blocker);
    }

    [Fact]
    public void Notifications_are_honest_that_they_cannot_be_checked_from_here()
    {
        var push = SetupRules.Build(Facts(), []).Capabilities.Single(c => c.Id == "push");

        Assert.Equal(CapabilityStatuses.Unsupported, push.Status);
        Assert.Contains("cannot check", push.Detail);
    }

    [Fact]
    public void The_support_bundle_holds_only_versions_and_states()
    {
        var bundle = SetupRules.Bundle(SetupRules.Build(Facts(chat: true), ["brief"]), "1.2.3", Now);

        Assert.Equal("1.2.3", bundle.Version);
        Assert.Contains("talk: done", bundle.Goals);
        Assert.Contains("brief: skipped", bundle.Goals);
        Assert.Contains("source: open", bundle.Goals);
        Assert.Equal("2026-10-17 09:00 UTC", bundle.GeneratedAt);
        Assert.Contains("no names", bundle.Note);
    }

    private sealed class World
    {
        public readonly InMemorySettingsStore Settings = new();
        public int Conversations;
        public int Reminders;
        public int LibraryItems;
        public bool BriefOn;
        public string[] Mcp = [];
        public int PushDevices;
        public bool BrokenApp;

        public SetupService Service => new(
            Fake<IConversationStore>.Create(("ListAsync", _ => (IReadOnlyList<Conversation>)
                Enumerable.Range(0, Conversations).Select(_ => new Conversation(Owner, "Chat")).ToArray())),
            Fake<IReminderService>.Create(("ListAsync", _ => (IReadOnlyList<ReminderRecord>)
                Enumerable.Range(0, Reminders).Select(i => new ReminderRecord(Guid.NewGuid(), Owner, "r", Now, "w", "scheduled", Now, null)).ToArray())),
            Fake<IDailyBriefingService>.Create(("GetAsync", _ => BriefOn
                ? new DailyBriefingPreferenceRecord(Owner, true, new TimeOnly(8, 0), "UTC", "", null, null)
                : null)),
            Fake<ILibraryService>.Create(("ListAsync", _ => (IReadOnlyList<LibraryItem>)
                Enumerable.Range(0, LibraryItems).Select(_ => new LibraryItem(Guid.NewGuid(), Owner, "note", null, "t", "", [], [], "c", "app", null, Now, Now)).ToArray())),
            Fake<IIntegrationCredentialStore>.Create(("GetStatusAsync", _ => null)),
            Fake<IUserMcpServerRegistry>.Create(("ListAsync", _ => (IReadOnlyList<UserMcpServer>)Mcp
                .Select(name => new UserMcpServer(name, name, "https://example.com", [], Now, false, IsValid: !BrokenApp)).ToArray())),
            Settings,
            Fake<IPushDeviceRepository>.Create(("CountAsync", _ => PushDevices)));
    }

    [Fact]
    public async Task The_service_reads_real_state_and_resumes_after_a_reconnect_without_repeating_done_steps()
    {
        var world = new World { Conversations = 1, Reminders = 2, LibraryItems = 1, BriefOn = true };

        var view = await world.Service.GetAsync(Owner, true, null, default);

        Assert.Equal(["talk", "reminder", "source", "brief"], view.Goals.Where(g => g.Done).Select(g => g.Id));
        Assert.Equal("calendar", view.NextGoalId);
        Assert.Contains("Arrives at 08:00", view.Capabilities.Single(c => c.Id == "brief").Detail);
    }

    [Fact]
    public async Task Skipping_is_stored_per_owner_only_for_optional_steps_and_can_be_undone()
    {
        var world = new World();

        Assert.True((await world.Service.DeclineAsync(Owner, "calendar", default)).Succeeded);
        Assert.True((await world.Service.DeclineAsync(Owner, "calendar", default)).Succeeded);
        Assert.Equal("goalId", (await world.Service.DeclineAsync(Owner, "talk", default)).Field);
        Assert.Equal("goalId", (await world.Service.DeclineAsync(Owner, "everything", default)).Field);

        var skipped = await world.Service.GetAsync(Owner, true, null, default);
        var other = await world.Service.GetAsync(Other, true, null, default);
        Assert.True(skipped.Goals.Single(g => g.Id == "calendar").Declined);
        Assert.False(other.Goals.Single(g => g.Id == "calendar").Declined);

        await world.Service.ReopenAsync(Owner, "calendar", default);
        Assert.False((await world.Service.GetAsync(Owner, true, null, default)).Goals.Single(g => g.Id == "calendar").Declined);
    }

    [Fact]
    public async Task Capabilities_state_push_apps_scanner_and_storage_honestly_when_the_server_looked()
    {
        var world = new World { Mcp = ["Calendar", "Mail"], BrokenApp = true, PushDevices = 0 };

        var view = await world.Service.GetAsync(Owner, true, null, default, new SetupEnvironment(false, false, true));
        string Status(string id) => view.Capabilities.Single(c => c.Id == id).Status;
        string Detail(string id) => view.Capabilities.Single(c => c.Id == id).Detail;

        Assert.Equal("unavailable", Status("push"));
        Assert.Contains("only appear in the app", Detail("push"));
        Assert.Equal("unavailable", Status("apps"));
        Assert.Contains("2 of 2 apps need attention", Detail("apps"));
        Assert.Equal("unavailable", Status("scanner"));
        Assert.Contains("uploads are refused", Detail("scanner"));
        Assert.Equal("ready", Status("storage"));
    }

    [Fact]
    public async Task A_registered_phone_is_ready_but_never_claimed_to_show_pushes()
    {
        var world = new World { PushDevices = 2 };

        var view = await world.Service.GetAsync(Owner, true, null, default, new SetupEnvironment(true, true, true));
        var push = view.Capabilities.Single(c => c.Id == "push");

        Assert.Equal("ready", push.Status);
        Assert.Contains("2 phones registered", push.Detail);
        Assert.Contains("not something Jarvis can see", push.Detail);
        Assert.Equal("not_connected", view.Capabilities.Single(c => c.Id == "apps").Status);
        var none = await new World().Service.GetAsync(Owner, true, null, default, new SetupEnvironment(true, null, null));
        Assert.Equal("not_connected", none.Capabilities.Single(c => c.Id == "push").Status);
        Assert.Equal("unsupported", none.Capabilities.Single(c => c.Id == "scanner").Status);
    }

    [Fact]
    public async Task Without_a_check_the_old_capabilities_are_unchanged_and_the_bundle_has_no_names()
    {
        var plain = await new World().Service.GetAsync(Owner, true, null, default);
        Assert.Equal("unsupported", plain.Capabilities.Single(c => c.Id == "push").Status);
        Assert.DoesNotContain(plain.Capabilities, c => c.Id is "apps" or "scanner" or "storage");

        var world = new World { Mcp = ["Secret Name"], BrokenApp = true };
        var view = await world.Service.GetAsync(Owner, true, null, default, new SetupEnvironment(true, true, true));
        var bundle = SetupRules.Bundle(view, "1.0", Now);
        Assert.DoesNotContain("Secret Name", string.Join(' ', bundle.Capabilities.Select(c => c.Detail)));
    }
}
