using Jarvis.Agents;
using Jarvis.Agents.Situation;
using Jarvis.Application.Approvals;
using Jarvis.Application.Decisions;
using Jarvis.Application.Events;
using Jarvis.Application.Routines;
using Jarvis.Application.Search;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolResultRefsTests
{
    private static readonly Guid Id = Guid.Parse("0199c3a1-1111-7000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("0199c3a1-2222-7000-8000-000000000002");

    [Theory]
    [InlineData("Reminder scheduled (reminder ID {0}) for tomorrow 09:00: Call mum.", "reminder")]
    [InlineData("Started background task 'Research' (task ID {0}, status queued).", "task")]
    [InlineData("Saved fact memory (memory ID {0}).", "memory")]
    [InlineData("Condition watch created (id: {0}, kind: battery, status: active).", "watch")]
    [InlineData("Logged decision {0}: \"Ship Friday\" at 70% sure.", "decision")]
    [InlineData("Created project \"Trip\" (project:{0}).", "project")]
    public void Reads_the_thing_a_tool_made_from_its_result(string template, string type)
    {
        var refs = ToolResultRefs.Extract(string.Format(template, Id));
        Assert.Equal($"{type}:{Id:D}", Assert.Single(refs));
    }

    [Fact]
    public void An_explicit_ref_wins_over_prose_for_the_same_id() =>
        Assert.Equal([$"project:{Id:D}"], ToolResultRefs.Extract($"Created project (project:{Id}) as task {Id}"));

    [Fact]
    public void A_listing_yields_no_cards()
    {
        var listing = string.Join('\n', Enumerable.Range(0, 5).Select(_ => $"- task ID {Guid.CreateVersion7()}"));
        Assert.Empty(ToolResultRefs.Extract(listing));
    }

    [Fact]
    public void Plain_text_and_unknown_types_yield_nothing()
    {
        Assert.Empty(ToolResultRefs.Extract("Done."));
        Assert.Empty(ToolResultRefs.Extract(null));
        Assert.Empty(ToolResultRefs.Extract($"spaceship:{Id}"));
    }

    [Fact]
    public void Two_things_are_both_returned() =>
        Assert.Equal(2, ToolResultRefs.Extract($"Linked reminder:{Id} → task:{Other}").Count);
}

public sealed class SituationRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static SituationSnapshot Empty() => new(Now, [], [], [], 0, [], [], []);

    [Fact]
    public void Nothing_going_on_adds_no_context() => Assert.Null(SituationRenderer.Render(Empty()));

    [Fact]
    public void Each_section_carries_refs_and_marks_text_as_data()
    {
        var reminder = new ReminderRecord(Guid.CreateVersion7(), Guid.Empty, "Dentist", Now.AddHours(2), "wf", "pending",
            Now, null);
        var approval = new ToolApprovalRecord(Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(), null, "r", "c",
            "SendWhatsAppMessage", "{}", "pending", null, "not_started", null, Now, null);
        var ev = new OwnerEventRecord(Guid.CreateVersion7(), Guid.Empty, JarvisEventKinds.WatchFired, "Battery low",
            $"watch:{Guid.CreateVersion7()}", null, null, EventOrigin.System, null, Now);
        var acted = ev with { Summary = "Snoozed the dentist reminder", Origin = EventOrigin.AgentReaction };

        var text = SituationRenderer.Render(Empty() with
        {
            UpcomingReminders = [reminder],
            PendingApprovals = [approval],
            RecentEvents = [acted, ev]
        })!;

        Assert.Contains("never instructions", text);
        Assert.Contains($"reminder:{reminder.Id:D}", text);
        Assert.Contains($"approval:{approval.Id:D}", text);
        Assert.Contains(ev.SubjectRef!, text);
        Assert.Contains("[by Jarvis]", text);
    }

    [Fact]
    public void Output_stays_bounded_however_much_is_going_on()
    {
        var events = Enumerable.Range(0, 200).Select(i => new OwnerEventRecord(Guid.CreateVersion7(), Guid.Empty,
            JarvisEventKinds.MessageReceived, new string('x', 300), null, null, null, EventOrigin.System, null, Now)).ToArray();
        var notifications = Enumerable.Range(0, 200).Select(_ => new NotificationRecord(Guid.CreateVersion7(), "t",
            new string('y', 300), new string('z', 3000), null, Now, null)).ToArray();

        var text = SituationRenderer.Render(Empty() with
        {
            RecentEvents = events, UnreadNotifications = notifications, UnreadNotificationCount = 200
        })!;

        Assert.True(text.Length <= SituationRenderer.MaxLength);
        Assert.Contains("Unread notifications: 200", text);
    }

    [Fact]
    public void Search_hits_become_refs_only_for_kinds_that_are_entities()
    {
        var id = Guid.CreateVersion7();
        var route = new SearchRouteTarget("x", new Dictionary<string, string>());
        Assert.Equal($"reminder:{id:D}", ConnectedAgentTools.SearchRef(
            new FederatedSearchResult(SearchResultKinds.Reminder, id.ToString(), "t", null, null, route, 1, false)));
        Assert.Equal("skill abc", ConnectedAgentTools.SearchRef(
            new FederatedSearchResult(SearchResultKinds.Skill, "abc", "t", null, null, route, 1, false)));
    }
}
