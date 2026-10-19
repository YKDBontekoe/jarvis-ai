using Jarvis.Application.Library;
using Jarvis.Application.Stories;
using Jarvis.Application.Timeline;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class StoryTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fc31");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fc32");
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Moment(string kind, string title, int daysAgo, string? detail = null) =>
        new($"{kind}-{title}", kind, title, detail, Now.AddDays(-daysAgo), Today.AddDays(-daysAgo), Guid.NewGuid());

    private sealed class Setup
    {
        public readonly List<TimelineEvent> Events = [];
        public readonly List<NoteDraft> Saved = [];
        public readonly InMemorySettingsStore Settings = new();
        public int DaysWithData = 20;
        public HashSet<string>? AskedKinds;
        public StoryService Service => new(Settings,
            Fake<ITimelineService>.Create(
                ("TodayAsync", _ => Today),
                ("QueryAsync", args =>
                {
                    var query = (TimelineQuery)args[1]!;
                    AskedKinds = query.Kinds?.ToHashSet();
                    var events = Events.Where(e => e.Date >= query.From && e.Date <= query.To &&
                                                   (query.Kinds is null || query.Kinds.Contains(e.Kind))).ToArray();
                    return new TimelineResult(query.From, query.To,
                        events.GroupBy(e => e.Date).Select(g => new TimelineDay(g.Key, g.ToArray())).ToArray(),
                        events.Length, false, []);
                }),
                ("InsightsAsync", _ => new TimelineInsights(Today.AddDays(-6), Today, DaysWithData,
                    [new TimelineInsight("i", "Mood is higher on days with exercise", "Across the window.", .6, [])], []))),
            Fake<ILibraryService>.Create(("AddNoteAsync", args =>
            {
                Saved.Add((NoteDraft)args[1]!);
                return LibraryOperation<LibraryItem>.Ok(new LibraryItem(Guid.NewGuid(), Owner, "report", null, "t", "", [],
                    [], "c", "app", null, Now, Now));
            })),
            new FixedClock());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static StoryRequest Week(IReadOnlyList<string>? kinds = null, string? style = null) =>
        new(Today.AddDays(-6), Today, style, kinds);

    [Fact]
    public async Task The_counts_are_of_real_moments_and_every_item_points_at_its_record()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Decision, "Switch bank", 5));
        s.Events.Add(Moment(TimelineKinds.Task, "Book the plumber", 3));
        s.Events.Add(Moment(TimelineKinds.Task, "Send the deck", 2));
        s.Events.Add(Moment(TimelineKinds.Journal, "Good day", 1, "Walked along the canal and felt calm."));

        var draft = (await s.Service.CreateAsync(Owner, Week(), default)).Value!;

        Assert.Equal(1, draft.Counts[TimelineKinds.Decision]);
        Assert.Equal(2, draft.Counts[TimelineKinds.Task]);
        Assert.Equal(1, draft.Counts[TimelineKinds.Journal]);
        Assert.Equal(4, draft.Items.Count);
        Assert.All(draft.Items, item => Assert.Matches("^(decision|task|journal):", item.SourceRef));
        Assert.Contains(draft.Items, i => i.Text == "Good day: Walked along the canal and felt calm.");
        Assert.Equal(draft.Items.OrderBy(i => i.Date).Select(i => i.Id), draft.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Money_contacts_and_chats_stay_out_unless_the_owner_chooses_them()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Expense, "Netflix", 2));
        s.Events.Add(Moment(TimelineKinds.Contact, "Talked to Rosa", 2));
        s.Events.Add(Moment(TimelineKinds.Task, "Do it", 2));

        var plain = (await s.Service.CreateAsync(Owner, Week(), default)).Value!;
        Assert.Equal([TimelineKinds.Task], plain.Items.Select(i => i.Kind));
        Assert.Equal(StoryRules.DefaultKinds.ToHashSet(), s.AskedKinds);

        var chosen = (await s.Service.CreateAsync(Owner, Week([TimelineKinds.Expense]), default)).Value!;
        Assert.Equal([TimelineKinds.Expense], chosen.Items.Select(i => i.Kind));
        Assert.DoesNotContain(TimelineKinds.Task, chosen.Counts.Keys);
    }

    [Fact]
    public async Task Patterns_carry_their_window_and_limits_and_are_left_out_when_the_data_is_thin()
    {
        var rich = new Setup { DaysWithData = 30 };
        var thin = new Setup { DaysWithData = 5 };

        var withData = (await rich.Service.CreateAsync(Owner, Week(), default)).Value!;
        var without = (await thin.Service.CreateAsync(Owner, Week(), default)).Value!;

        var pattern = Assert.Single(withData.Patterns);
        Assert.Contains("30 days with data", pattern.Limits);
        Assert.Contains("not why", pattern.Limits);
        Assert.Empty(without.Patterns);
        Assert.Contains("too few to say anything", without.PatternsNote);
    }

    [Fact]
    public async Task Bad_windows_styles_and_kinds_are_refused_and_other_owners_stories_are_invisible()
    {
        var s = new Setup();

        Assert.Equal("from", (await s.Service.CreateAsync(Owner, new StoryRequest(Today, Today.AddDays(-1), null, null), default)).Field);
        Assert.Equal("to", (await s.Service.CreateAsync(Owner, new StoryRequest(Today, Today.AddDays(1), null, null), default)).Field);
        Assert.Equal("from", (await s.Service.CreateAsync(Owner, new StoryRequest(Today.AddDays(-200), Today, null, null), default)).Field);
        Assert.Equal("style", (await s.Service.CreateAsync(Owner, Week(style: "epic"), default)).Field);
        Assert.Equal("kinds", (await s.Service.CreateAsync(Owner, Week(["dreams"]), default)).Field);
        var id = (await s.Service.CreateAsync(Owner, Week(), default)).Value!.Id;
        Assert.Null(await s.Service.GetAsync(Other, id, default));
        Assert.False(await s.Service.DeleteAsync(Other, id, default));
        Assert.Equal(StoryFailure.NotFound, (await s.Service.SaveAsync(Other, id, default)).Failure);
    }

    [Fact]
    public async Task Editing_wording_keeps_the_original_and_removing_or_moving_changes_only_the_story()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Task, "Book the plumber", 3));
        s.Events.Add(Moment(TimelineKinds.Task, "Send the deck", 2));
        var draft = (await s.Service.CreateAsync(Owner, Week(), default)).Value!;
        var first = draft.Items[0];

        var edited = (await s.Service.EditItemAsync(Owner, draft.Id, first.Id, "We finally got the plumber booked", default)).Value!;
        Assert.Equal("We finally got the plumber booked", edited.Items[0].Text);
        Assert.Equal("Book the plumber", edited.Items[0].OriginalText);
        Assert.Equal(first.SourceRef, edited.Items[0].SourceRef);

        var removed = (await s.Service.SetRemovedAsync(Owner, draft.Id, first.Id, true, default)).Value!;
        Assert.True(removed.Items[0].Removed);
        var restored = (await s.Service.SetRemovedAsync(Owner, draft.Id, first.Id, false, default)).Value!;
        Assert.False(restored.Items[0].Removed);

        var moved = (await s.Service.MoveItemAsync(Owner, draft.Id, first.Id, "down", default)).Value!;
        Assert.Equal(first.Id, moved.Items[1].Id);
        Assert.Equal(StoryFailure.Conflict, (await s.Service.MoveItemAsync(Owner, draft.Id, first.Id, "down", default)).Failure);
        Assert.Equal("direction", (await s.Service.MoveItemAsync(Owner, draft.Id, first.Id, "sideways", default)).Field);
        Assert.Equal("text", (await s.Service.EditItemAsync(Owner, draft.Id, first.Id, "  ", default)).Field);
        Assert.Equal(StoryFailure.NotFound, (await s.Service.EditItemAsync(Owner, draft.Id, "nope", "x", default)).Failure);
    }

    [Fact]
    public async Task One_reflection_not_a_task_list_and_it_follows_what_is_left_in_the_story()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Decision, "Switch bank", 5));
        s.Events.Add(Moment(TimelineKinds.Task, "Do it", 2));
        var draft = (await s.Service.CreateAsync(Owner, Week(), default)).Value!;
        Assert.Contains("Switch bank", draft.Reflection);

        var decisionItem = draft.Items.Single(i => i.Kind == TimelineKinds.Decision);
        var after = (await s.Service.SetRemovedAsync(Owner, draft.Id, decisionItem.Id, true, default)).Value!;

        Assert.DoesNotContain("Switch bank", after.Reflection);
        Assert.Contains("matter", after.Reflection);
    }

    [Fact]
    public async Task Saving_writes_a_private_report_per_version_once()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Task, "Do it", 2));
        var id = (await s.Service.CreateAsync(Owner, Week(), default)).Value!.Id;

        var first = (await s.Service.SaveAsync(Owner, id, default)).Value!;
        await s.Service.SaveAsync(Owner, id, default);
        await s.Service.EditItemAsync(Owner, id, "i1", "Edited", default);
        await s.Service.SaveAsync(Owner, id, default);

        Assert.Equal(2, s.Saved.Count);
        Assert.Contains("private", s.Saved[0].Tags!);
        Assert.EndsWith("(v1)", s.Saved[0].Title);
        Assert.EndsWith("(v2)", s.Saved[1].Title);
        Assert.Equal(1, first.SavedVersion);
    }

    [Fact]
    public async Task A_shareable_export_leaves_out_what_was_chosen_including_the_counts_and_patterns()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Journal, "Rough day", 2, "Felt low and talked to Rosa."));
        s.Events.Add(Moment(TimelineKinds.Contact, "Talked to Rosa", 2));
        s.Events.Add(Moment(TimelineKinds.Task, "Send the deck", 1));
        var id = (await s.Service.CreateAsync(Owner, Week([TimelineKinds.Journal, TimelineKinds.Contact, TimelineKinds.Task]), default)).Value!.Id;

        var full = (await s.Service.ExportAsync(Owner, id, null, default)).Value!;
        var shared = (await s.Service.ExportAsync(Owner, id, "journal,people", default)).Value!;

        Assert.Contains("Felt low", full.Content);
        Assert.Contains("Patterns", full.Content);
        Assert.DoesNotContain("Felt low", shared.Content);
        Assert.DoesNotContain("Rosa", shared.Content);
        Assert.Contains("Send the deck", shared.Content);
        Assert.DoesNotContain("Patterns", shared.Content);
        Assert.Contains("A shareable version: birthday, contact, journal left out.", shared.Content);
        Assert.Equal(2, shared.Left);
        Assert.Equal("redact", (await s.Service.ExportAsync(Owner, id, "dreams", default)).Field);
        Assert.Empty(s.Saved);
    }

    [Fact]
    public async Task Removed_moments_stay_out_of_every_copy()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Task, "Private errand", 2));
        s.Events.Add(Moment(TimelineKinds.Task, "Public thing", 1));
        var draft = (await s.Service.CreateAsync(Owner, Week(), default)).Value!;
        await s.Service.SetRemovedAsync(Owner, draft.Id, draft.Items.First(i => i.Text == "Private errand").Id, true, default);
        await s.Service.SaveAsync(Owner, draft.Id, default);

        var export = (await s.Service.ExportAsync(Owner, draft.Id, null, default)).Value!;

        Assert.DoesNotContain("Private errand", export.Content);
        Assert.DoesNotContain("Private errand", s.Saved.Single().Content);
        Assert.Contains("Public thing", export.Content);
    }

    [Fact]
    public async Task The_agent_tool_drafts_from_real_moments_and_refuses_bad_dates()
    {
        var s = new Setup();
        s.Events.Add(Moment(TimelineKinds.Task, "Book the plumber", 2));
        var tools = new Jarvis.Agents.Stories.StoryAgentTools(s.Service,
            new FixedUser());

        var text = await tools.DraftLifeStoryAsync(Today.AddDays(-6).ToString("yyyy-MM-dd"), Today.ToString("yyyy-MM-dd"));

        Assert.Contains("Book the plumber", text);
        Assert.Contains("not instructions", text);
        Assert.Equal("Dates must be yyyy-MM-dd.", await tools.DraftLifeStoryAsync("soon", "later"));
    }

    private sealed class FixedUser : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
