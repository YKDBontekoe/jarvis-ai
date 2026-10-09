using Jarvis.Application.Automations;
using Jarvis.Application.Events;
using Jarvis.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class EntityRefTests
{
    [Fact]
    public void Round_trips_through_its_string_form()
    {
        var id = Guid.CreateVersion7();
        var entity = new EntityRef(EntityTypes.Task, id);
        Assert.Equal($"task:{id:D}", entity.ToString());
        Assert.True(EntityRef.TryParse(entity.ToString(), out var parsed));
        Assert.Equal(entity, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("task")]
    [InlineData("task:")]
    [InlineData("task:not-a-guid")]
    [InlineData("spaceship:0190a1b2-0000-7000-8000-000000000001")]
    [InlineData("task:00000000-0000-0000-0000-000000000000")]
    public void Rejects_anything_that_is_not_a_known_ref(string? value) =>
        Assert.False(EntityRef.TryParse(value, out _));

    [Fact]
    public void Parsing_ignores_type_case_and_whitespace()
    {
        var id = Guid.CreateVersion7();
        Assert.True(EntityRef.TryParse($" Reminder : {id} ", out var parsed));
        Assert.Equal(new EntityRef(EntityTypes.Reminder, id), parsed);
    }
}

public sealed class JarvisEventBusTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public async Task Every_matching_handler_runs_even_when_one_fails()
    {
        var first = new RecordingHandler();
        var failing = new RecordingHandler { Throw = true };
        var last = new RecordingHandler();
        var skipping = new RecordingHandler { Accept = false };
        var bus = new JarvisEventBus([first, failing, skipping, last], NullLogger<JarvisEventBus>.Instance);

        await bus.PublishAsync(new JarvisEvent(Owner, JarvisEventKinds.WatchFired, "Battery low"), CancellationToken.None);

        Assert.Single(first.Seen);
        Assert.Single(failing.Seen);
        Assert.Empty(skipping.Seen);
        Assert.Single(last.Seen);
    }

    [Fact]
    public async Task Events_are_normalized_before_handlers_see_them()
    {
        var handler = new RecordingHandler();
        var bus = new JarvisEventBus([handler], NullLogger<JarvisEventBus>.Instance);

        await bus.PublishAsync(new JarvisEvent(Owner, " Task.Failed ", new string('x', 1000),
            Data: Enumerable.Range(0, 40).ToDictionary(i => $"k{i}", i => new string('y', 2000))), CancellationToken.None);

        var seen = Assert.Single(handler.Seen);
        Assert.Equal(JarvisEventKinds.TaskFailed, seen.Kind);
        Assert.NotNull(seen.Id);
        Assert.NotNull(seen.At);
        Assert.True(seen.Summary.Length <= JarvisEventLimits.MaxSummaryLength);
        Assert.Equal(JarvisEventLimits.MaxDataEntries, seen.Data!.Count);
        Assert.All(seen.Data.Values, value => Assert.True(value.Length <= JarvisEventLimits.MaxDataValueLength));
    }

    [Fact]
    public async Task An_event_without_an_owner_is_refused()
    {
        var bus = new JarvisEventBus([], NullLogger<JarvisEventBus>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bus.PublishAsync(new JarvisEvent(Guid.Empty, JarvisEventKinds.WatchFired, "x"), CancellationToken.None));
    }

    [Fact]
    public async Task Try_publish_never_reaches_the_caller()
    {
        IJarvisEventBus? none = null;
        await none.TryPublishAsync(new JarvisEvent(Owner, "x", "y"), CancellationToken.None);
        var throwing = Fake<IJarvisEventBus>.Create(("PublishAsync", _ => throw new InvalidOperationException()));
        await throwing.TryPublishAsync(new JarvisEvent(Owner, "x", "y"), CancellationToken.None);
    }

    [Fact]
    public async Task Conversation_links_are_drawn_only_for_things_jarvis_made_in_a_chat()
    {
        var added = new List<(EntityRef From, EntityRef To)>();
        var links = Fake<IEntityLinkRepository>.Create(("AddAsync", args =>
        {
            added.Add(((EntityRef)args[1]!, (EntityRef)args[2]!));
            return true;
        }));
        var handler = new ConversationLinkHandler(links);
        var conversation = Guid.CreateVersion7();
        var reminder = new EntityRef(EntityTypes.Reminder, Guid.CreateVersion7());

        var byAgent = new JarvisEvent(Owner, JarvisEventKinds.ReminderDue, "x", reminder, ConversationId: conversation,
            Origin: EventOrigin.Agent);
        var byOwner = byAgent with { Origin = EventOrigin.Owner };
        var noChat = byAgent with { ConversationId = null };

        Assert.True(handler.Handles(byAgent));
        Assert.False(handler.Handles(byOwner));
        Assert.False(handler.Handles(noChat));
        await handler.HandleAsync(byAgent, CancellationToken.None);
        Assert.Equal((new EntityRef(EntityTypes.Conversation, conversation), reminder), Assert.Single(added));
    }

    private sealed class RecordingHandler : IJarvisEventHandler
    {
        public List<JarvisEvent> Seen { get; } = [];
        public bool Throw { get; init; }
        public bool Accept { get; init; } = true;

        public bool Handles(JarvisEvent ev) => Accept;

        public Task HandleAsync(JarvisEvent ev, CancellationToken cancellationToken)
        {
            Seen.Add(ev);
            if (Throw) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }
    }
}

public sealed class AutomationEventBridgeTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Theory]
    [InlineData(AutomationEventKinds.TaskCompleted, JarvisEventKinds.TaskCompleted, EntityTypes.Task)]
    [InlineData(AutomationEventKinds.FileUploaded, JarvisEventKinds.FileUploaded, EntityTypes.File)]
    [InlineData(AutomationEventKinds.JournalSaved, JarvisEventKinds.JournalSaved, EntityTypes.Journal)]
    [InlineData(AutomationEventKinds.ExpenseLogged, JarvisEventKinds.ExpenseLogged, EntityTypes.Expense)]
    [InlineData(AutomationEventKinds.InboxNeedsReply, JarvisEventKinds.InboxNeedsReply, null)]
    [InlineData(AutomationEventKinds.MessageReceived, JarvisEventKinds.MessageReceived, null)]
    [InlineData(AutomationEventKinds.Webhook, JarvisEventKinds.WebhookCalled, null)]
    public void Every_automation_kind_maps_onto_the_spine(string automationKind, string spineKind, string? subjectType)
    {
        var id = Guid.CreateVersion7();
        var ev = AutomationEventBridge.ToSpine(Owner,
            new AutomationEvent(automationKind, "Title", "Detail", "src", id, DateTimeOffset.UnixEpoch))!;
        Assert.Equal(spineKind, ev.Kind);
        Assert.Equal(Owner, ev.OwnerId);
        Assert.Equal("Detail", ev.Data!["detail"]);
        Assert.Equal(subjectType, ev.Subject?.Type);
        if (subjectType is not null) Assert.Equal(id, ev.Subject!.Value.Id);
    }

    [Fact]
    public async Task Automations_still_run_and_decide_the_count_while_the_spine_hears_the_event()
    {
        var spineEvents = new List<JarvisEvent>();
        var automations = Fake<IAutomationEventBus>.Create(("PublishAsync", _ => 2));
        var spine = Fake<IJarvisEventBus>.Create(("PublishAsync", args =>
        {
            spineEvents.Add((JarvisEvent)args[0]!);
            return Task.CompletedTask;
        }));
        var bridge = new AutomationEventBridge(automations, spine);

        var started = await bridge.PublishAsync(Owner,
            new AutomationEvent(AutomationEventKinds.ExpenseLogged, "EUR 12.00"), CancellationToken.None);

        Assert.Equal(2, started);
        Assert.Equal(JarvisEventKinds.ExpenseLogged, Assert.Single(spineEvents).Kind);
    }

    [Fact]
    public async Task A_failing_spine_never_breaks_automations()
    {
        var automations = Fake<IAutomationEventBus>.Create(("PublishAsync", _ => 1));
        var spine = Fake<IJarvisEventBus>.Create(("PublishAsync", _ => throw new InvalidOperationException()));
        Assert.Equal(1, await new AutomationEventBridge(automations, spine).PublishAsync(Owner,
            new AutomationEvent(AutomationEventKinds.Webhook, "hook"), CancellationToken.None));
    }
}
