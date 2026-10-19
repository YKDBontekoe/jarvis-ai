using Jarvis.Agents.Apps;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AppGoalTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fe01");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("help with my calendar", "calendar")]
    [InlineData("what appointments do I have", "calendar")]
    [InlineData("kalender", "calendar")]
    [InlineData("deal with my email", "mail")]
    [InlineData("reply to messages", "mail")]
    [InlineData("phone number of someone", "contacts")]
    [InlineData("contacten", "contacts")]
    public void A_goal_in_plain_words_finds_the_app_that_can_do_it(string query, string goal) =>
        Assert.Contains(AppGoalCatalog.Match(query), item => item.Id == goal);

    [Fact]
    public void An_empty_search_lists_every_goal_and_unrelated_words_match_none()
    {
        Assert.Equal(AppGoalCatalog.All.Count, AppGoalCatalog.Match(null).Count);
        Assert.Equal(AppGoalCatalog.All.Count, AppGoalCatalog.Match("  ").Count);
        Assert.Empty(AppGoalCatalog.Match("track my parcel deliveries"));
    }

    [Fact]
    public void Capabilities_come_from_the_tools_the_pack_really_has()
    {
        var calendar = AppGoalCatalog.Preview(IntegrationPackCatalog.Find("calendar")!);
        var mail = AppGoalCatalog.Preview(IntegrationPackCatalog.Find("mail")!);
        var contacts = AppGoalCatalog.Preview(IntegrationPackCatalog.Find("contacts")!);

        Assert.Equal(["calendar events list"], calendar.Reads);
        Assert.Equal(["calendar event create"], calendar.Changes);
        Assert.Equal(["list emails", "search emails"], mail.Reads);
        Assert.Equal(["draft email"], mail.Prepares);
        Assert.Empty(mail.Changes); // Jarvis can draft but this pack cannot send
        Assert.Contains("never sees your password", mail.Needs);
        Assert.Contains("private calendar link", calendar.Needs);
        Assert.Contains("no ready-made connector", contacts.Needs);
    }

    [Fact]
    public void The_next_step_is_the_real_one_and_a_pack_without_a_connector_says_so()
    {
        Assert.Contains("private link", AppGoalCatalog.NextStep(IntegrationPackCatalog.Find("calendar")!, false));
        Assert.Contains("sign in once", AppGoalCatalog.NextStep(IntegrationPackCatalog.Find("mail")!, false));
        Assert.Contains("no ready-made connector", AppGoalCatalog.NextStep(IntegrationPackCatalog.Find("contacts")!, false));
        Assert.StartsWith("Connected", AppGoalCatalog.NextStep(IntegrationPackCatalog.Find("mail")!, true));
    }

    private sealed class Setup
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly HashSet<string> IcsFor = [];
        public readonly HashSet<string> McpFor = [];
        public readonly List<CalendarEventRecord> Events = [];
        public (DateTimeOffset From, DateTimeOffset Until)? Asked;
        public bool CalendarDown;

        public AppGoalService Service => new(
            Fake<IIntegrationCredentialStore>.Create(("GetStatusAsync", args =>
            {
                var provider = (string)args[1]!;
                var id = provider.Replace("jarvis-pack-", string.Empty);
                return IcsFor.Contains(id)
                    ? new IntegrationCredentialStatus(provider, ["ics_url"], Now)
                    : null;
            })),
            Fake<IUserMcpServerRegistry>.Create(("ListAsync", _ => (IReadOnlyList<UserMcpServer>)McpFor
                .Select(name => new UserMcpServer(name, name, "https://example.com", [], Now, false)).ToArray())),
            Fake<ICalendarFeed>.Create(("ListUpcomingAsync", args =>
            {
                Asked = ((DateTimeOffset)args[1]!, (DateTimeOffset)args[2]!);
                if (CalendarDown) throw new HttpRequestException("down");
                return (IReadOnlyList<CalendarEventRecord>)Events;
            })),
            Settings);
    }

    [Fact]
    public async Task Nothing_connected_means_next_steps_and_no_try_button_or_fake_success()
    {
        var s = new Setup();

        var search = await s.Service.SearchAsync(Owner, "calendar", default);

        var goal = Assert.Single(search.Goals);
        Assert.Equal(AppGoalStatuses.NotConnected, goal.Status);
        Assert.Null(goal.Try);
        Assert.False(goal.Pinned);
        Assert.Contains("private link", goal.NextStep);
        Assert.Null(await s.Service.TryAsync(Owner, "calendar", Now, default));
        Assert.False(await s.Service.PinAsync(Owner, "calendar", default));
    }

    [Fact]
    public async Task A_saved_calendar_link_makes_it_ready_and_trying_reads_todays_events_for_real()
    {
        var s = new Setup();
        s.IcsFor.Add("calendar");
        s.Events.Add(new CalendarEventRecord("Standup", Now.AddHours(1), Now.AddHours(2)));

        var goal = Assert.Single((await s.Service.SearchAsync(Owner, "calendar", default)).Goals);
        var result = await s.Service.TryAsync(Owner, "calendar", Now, default);

        Assert.Equal(AppGoalStatuses.Ready, goal.Status);
        Assert.Equal("What is on today?", goal.Try!.Label);
        Assert.Equal("Standup", Assert.Single(result!.Events).Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero), s.Asked!.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), s.Asked.Value.Until);
    }

    [Fact]
    public async Task A_calendar_that_cannot_be_read_gives_a_targeted_next_step_not_an_empty_day()
    {
        var s = new Setup();
        s.IcsFor.Add("calendar");
        s.CalendarDown = true;

        var result = await s.Service.TryAsync(Owner, "calendar", Now, default);

        Assert.Empty(result!.Events);
        Assert.Contains("could not be read", result.Problem);
        Assert.Contains("link you saved", result.Problem);
    }

    [Fact]
    public async Task Mail_needs_a_connected_server_and_its_try_is_a_read_only_chat_prompt()
    {
        var s = new Setup();
        s.IcsFor.Add("mail"); // a stored credential alone cannot read mail
        Assert.Equal(AppGoalStatuses.NotConnected,
            Assert.Single((await s.Service.SearchAsync(Owner, "email", default)).Goals).Status);

        s.McpFor.Add("Mail");
        var result = await s.Service.TryAsync(Owner, "mail", Now, default);

        Assert.Equal(AppTryKinds.ChatPrompt, result!.Kind);
        Assert.Contains("Only read", result.Prompt);
        Assert.Contains("do not send", result.Prompt);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task An_unrelated_goal_is_described_honestly_with_no_pretend_connector()
    {
        var search = await new Setup().Service.SearchAsync(Owner, "track my parcel deliveries", default);

        Assert.True(search.Unmatched);
        Assert.Empty(search.Goals);
        Assert.Contains("Nothing has been connected", search.Suggestion);
    }

    [Fact]
    public async Task Pinning_needs_a_connected_app_is_limited_and_unpinning_undoes_it()
    {
        var s = new Setup();
        s.IcsFor.Add("calendar");
        s.McpFor.Add("Mail");

        Assert.True(await s.Service.PinAsync(Owner, "calendar", default));
        Assert.True(await s.Service.PinAsync(Owner, "calendar", default)); // again is the same pin
        Assert.True(await s.Service.PinAsync(Owner, "mail", default));
        Assert.False(await s.Service.PinAsync(Owner, "contacts", default)); // not connected
        Assert.False(await s.Service.PinAsync(Owner, "weather", default)); // unknown

        var pinned = (await s.Service.SearchAsync(Owner, null, default)).Goals.Where(g => g.Pinned).Select(g => g.Id);
        Assert.Equal(["calendar", "mail"], pinned);
        Assert.True(await s.Service.UnpinAsync(Owner, "calendar", default));
        Assert.False(await s.Service.UnpinAsync(Owner, "calendar", default));
    }

    [Fact]
    public async Task Pins_are_per_owner()
    {
        var s = new Setup();
        s.IcsFor.Add("calendar");
        await s.Service.PinAsync(Owner, "calendar", default);

        var other = await s.Service.SearchAsync(Guid.NewGuid(), "calendar", default);

        Assert.False(Assert.Single(other.Goals).Pinned);
    }

    [Fact]
    public async Task The_tool_lists_real_capabilities_and_next_steps_and_is_honest_about_gaps()
    {
        var s = new Setup();
        var tools = new AppGoalAgentTools(s.Service, new FixedUser());

        var calendar = await tools.FindAppForGoalAsync("manage my calendar");
        var gap = await tools.FindAppForGoalAsync("track my parcel deliveries");

        Assert.Contains("Manage my calendar via Calendar: not connected", calendar);
        Assert.Contains("Changes: calendar event create", calendar);
        Assert.Contains("Next: Paste your calendar's private link", calendar);
        Assert.Contains("Nothing has been connected", gap);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
