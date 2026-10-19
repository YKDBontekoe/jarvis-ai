using Jarvis.Agents;
using Jarvis.Agents.Improvements;
using Jarvis.Application.Improvements;
using Jarvis.Application.Learning;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class LessonTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000c1a1");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 3, 0, 0, TimeSpan.Zero);

    private static TurnTraceRecord Trace(string tool, string outcome = ToolOutcomes.Completed, double hoursAgo = 24,
        Guid? conversation = null, Guid? message = null, Guid[]? lessons = null) =>
        new(Guid.NewGuid(), Owner, conversation ?? Guid.NewGuid(), message ?? Guid.NewGuid(), null,
            TurnKinds.Interactive, [], [], [new TurnToolCall(tool, outcome, 80)], 500, "completed",
            Now.AddHours(-hoursAgo), null, lessons);

    private static LearningSignalRecord Signal(string kind, Guid? message = null, Guid? conversation = null,
        string? tool = null, string? errorKind = null, double hoursAgo = 23) =>
        new(Guid.NewGuid(), Owner, kind, conversation ?? Guid.NewGuid(), message, null, tool, null, errorKind,
            Now.AddHours(-hoursAgo));

    // ---- mining ----

    [Fact]
    public void A_tool_that_failed_in_two_turns_is_a_finding_with_its_error_kinds()
    {
        var traces = new[] { Trace("SearchMail", ToolOutcomes.Failed), Trace("SearchMail", ToolOutcomes.Failed, 30) };
        var signals = new[]
        {
            Signal(LearningSignalKinds.ToolFailure, tool: "SearchMail", errorKind: "input"),
            Signal(LearningSignalKinds.ToolFailure, tool: "SearchMail", errorKind: "transient"),
            Signal(LearningSignalKinds.ToolFailure, tool: "SearchMail", errorKind: "input")
        };

        var finding = Assert.Single(LessonMinerEngine.Mine(traces, signals, Now));

        Assert.Equal(("SearchMail", 2), (finding.Tool, finding.Incidents));
        Assert.Equal([LessonReasons.ToolFailed], finding.Reasons);
        Assert.Equal(["input", "transient"], finding.ErrorKinds);
    }

    [Fact]
    public void One_incident_old_incidents_and_successful_tools_are_not_findings()
    {
        var traces = new[]
        {
            Trace("OnlyOnce", ToolOutcomes.Failed),
            Trace("Old", ToolOutcomes.Failed, hoursAgo: 24 * 8), Trace("Old", ToolOutcomes.Failed, hoursAgo: 24 * 9),
            Trace("Fine"), Trace("Fine")
        };

        Assert.Empty(LessonMinerEngine.Mine(traces, [], Now));
    }

    [Fact]
    public void Thumbs_down_regenerate_and_correction_blame_the_tools_of_that_turn()
    {
        var conversation = Guid.NewGuid();
        var down = Trace("PlanDay", message: Guid.NewGuid());
        var regenerated = Trace("PlanDay", message: Guid.NewGuid());
        var corrected = Trace("PlanDay", conversation: conversation, hoursAgo: 5);
        var happy = Trace("PlanDay", message: Guid.NewGuid());
        var signals = new[]
        {
            Signal(LearningSignalKinds.ThumbsDown, down.MessageId),
            Signal(LearningSignalKinds.Regenerate, regenerated.MessageId),
            Signal(LearningSignalKinds.Correction, Guid.NewGuid(), conversation, hoursAgo: 4),
            Signal(LearningSignalKinds.ThumbsDown, happy.MessageId, hoursAgo: 10),
            Signal(LearningSignalKinds.ThumbsUp, happy.MessageId, hoursAgo: 9) // the last thumb wins
        };

        var finding = Assert.Single(LessonMinerEngine.Mine([down, regenerated, corrected, happy], signals, Now));

        Assert.Equal(3, finding.Incidents);
        Assert.Equal([LessonReasons.Corrected, LessonReasons.Regenerated, LessonReasons.ThumbsDown], finding.Reasons);
    }

    [Fact]
    public void A_correction_belongs_to_the_reply_just_before_it_in_that_conversation_only()
    {
        var conversation = Guid.NewGuid();
        var earlier = Trace("A", conversation: conversation, hoursAgo: 8);
        var reply = Trace("B", conversation: conversation, hoursAgo: 6);
        var otherConversation = Trace("C", hoursAgo: 5);
        var later = Trace("D", conversation: conversation, hoursAgo: 2);
        var signals = Enumerable.Range(0, 2).Select(_ =>
            Signal(LearningSignalKinds.Correction, Guid.NewGuid(), conversation, hoursAgo: 5)).ToArray();

        var findings = LessonMinerEngine.Mine([earlier, reply, otherConversation, later], signals, Now);

        // Two corrections on the same reply are one turn; nothing reaches the two-turn bar.
        Assert.Empty(findings);
    }

    [Fact]
    public void Declined_approvals_count_against_the_tool_and_findings_are_ordered_by_incidents()
    {
        var signals = new[]
        {
            Signal(LearningSignalKinds.ApprovalDenied, tool: "SendEmail"),
            Signal(LearningSignalKinds.ApprovalDenied, tool: "SendEmail"),
            Signal(LearningSignalKinds.ApprovalDenied, tool: "SendEmail"),
            Signal(LearningSignalKinds.ApprovalDenied, tool: "Browse"),
            Signal(LearningSignalKinds.ApprovalDenied, tool: "Browse")
        };

        var findings = LessonMinerEngine.Mine([], signals, Now);

        Assert.Equal(["SendEmail", "Browse"], findings.Select(f => f.Tool));
        Assert.Equal(3, findings[0].Incidents);
        Assert.Equal(0.8, LessonMinerEngine.Confidence(findings[0]), 3);
        Assert.Equal(0.7, LessonMinerEngine.Confidence(findings[1]), 3);
    }

    // ---- counting ----

    [Fact]
    public void A_lesson_is_credited_and_blamed_only_for_rated_turns_and_each_turn_is_counted_once()
    {
        var id = Guid.NewGuid();
        var up = Trace("T", message: Guid.NewGuid(), lessons: [id]);
        var down = Trace("T", message: Guid.NewGuid(), lessons: [id]);
        var unrated = Trace("T", message: Guid.NewGuid(), lessons: [id]);
        var elsewhere = Trace("T", message: Guid.NewGuid(), lessons: [Guid.NewGuid()]);
        var signals = new[]
        {
            Signal(LearningSignalKinds.ThumbsUp, up.MessageId), Signal(LearningSignalKinds.ThumbsDown, down.MessageId),
            Signal(LearningSignalKinds.ThumbsDown, elsewhere.MessageId)
        };
        var inForce = new Dictionary<Guid, LessonPayload> { [id] = new("Check inputs.", ["T"]) };

        var tally = LessonCounterEngine.Tally([up, down, unrated, elsewhere], signals, inForce, Now)[id];

        Assert.Equal((1, 1), (tally.Helpful, tally.Harmful));
        Assert.Equal(Now - LessonRules.CountingDelay, tally.CountedThrough);

        var again = LessonCounterEngine.Tally([up, down, unrated, elsewhere], signals,
            new Dictionary<Guid, LessonPayload> { [id] = inForce[id] with { CountedThrough = tally.CountedThrough } }, Now)[id];
        Assert.Equal((0, 0), (again.Helpful, again.Harmful));
    }

    [Fact]
    public void Turns_too_recent_to_have_been_rated_wait_for_the_next_count()
    {
        var id = Guid.NewGuid();
        var fresh = Trace("T", hoursAgo: 1, message: Guid.NewGuid(), lessons: [id]);
        var signals = new[] { Signal(LearningSignalKinds.ThumbsDown, fresh.MessageId, hoursAgo: 0.5) };

        var tally = LessonCounterEngine.Tally([fresh], signals,
            new Dictionary<Guid, LessonPayload> { [id] = new("x", ["T"]) }, Now)[id];

        Assert.Equal((0, 0), (tally.Helpful, tally.Harmful));
        Assert.Equal(fresh.CreatedAt, tally.LastInjectedAt);
    }

    [Fact]
    public void A_corrected_or_regenerated_turn_counts_against_the_lesson_it_showed()
    {
        var id = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        var corrected = Trace("T", conversation: conversation, hoursAgo: 20, lessons: [id]);
        var regenerated = Trace("T", message: Guid.NewGuid(), lessons: [id]);
        var signals = new[]
        {
            Signal(LearningSignalKinds.Correction, Guid.NewGuid(), conversation, hoursAgo: 19),
            Signal(LearningSignalKinds.Regenerate, regenerated.MessageId)
        };

        var tally = LessonCounterEngine.Tally([corrected, regenerated], signals,
            new Dictionary<Guid, LessonPayload> { [id] = new("x", ["T"]) }, Now)[id];

        Assert.Equal((0, 2), (tally.Helpful, tally.Harmful));
    }

    // ---- drafting ----

    [Theory]
    [InlineData("{\"lesson\": \"Check the date range before searching the calendar.\"}", "Check the date range before searching the calendar.")]
    [InlineData("```json\n{\"lesson\": \"  Ask which   list   first. \"}\n```", "Ask which list first.")]
    [InlineData("{\"lesson\": \"short\"}", null)]
    [InlineData("{\"lesson\": \"See https://example.com for the steps to follow here.\"}", null)]
    [InlineData("{\"lesson\": \"Use api key sk-live-0123456789abcdef0123456789abcdef to call it.\"}", null)]
    [InlineData("{\"other\": \"Check the inputs before calling it.\"}", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void Drafts_are_cleaned_and_unsafe_or_unusable_ones_are_dropped(string reply, string? expected) =>
        Assert.Equal(expected, ModelLessonDrafter.Parse(reply));

    [Fact]
    public void A_draft_over_the_length_limit_is_dropped()
    {
        var long_ = "{\"lesson\": \"" + string.Join(' ', Enumerable.Repeat("careful", 50)) + "\"}";

        Assert.Null(ModelLessonDrafter.Parse(long_));
    }

    // ---- selection and injection ----

    private static LessonEntry Entry(string text, string tool, int helpful = 0, int harmful = 0, int daysOld = 1) =>
        new(Guid.NewGuid(), new LessonPayload(text, [tool], helpful, harmful), Now.AddDays(-daysOld));

    [Fact]
    public void Lessons_about_feature_tools_show_only_while_their_group_is_active()
    {
        var money = Entry("Log the amount in the user's currency.", "LogExpense");
        var core = Entry("Search before you answer from memory.", "SearchMemory");

        var plain = LessonContextContributor.Select([money, core], new HashSet<string>());
        var withMoney = LessonContextContributor.Select([money, core], new HashSet<string> { "money" });

        Assert.Equal([core], plain);
        Assert.Equal(2, withMoney.Count);
    }

    [Fact]
    public void Lessons_are_ordered_by_how_they_were_rated_and_capped_by_count_and_size()
    {
        var weak = Entry("weak lesson", "SearchMemory", helpful: 0, harmful: 2);
        var strong = Entry("strong lesson", "SearchMemory", helpful: 5);
        var many = Enumerable.Range(0, 8).Select(i => Entry($"filler {i}", "SearchMemory", helpful: 1)).ToList();

        var chosen = LessonContextContributor.Select([weak, .. many, strong], new HashSet<string>());

        Assert.Equal(LessonContextContributor.MaxLessons, chosen.Count);
        Assert.Equal(strong, chosen[0]);
        Assert.DoesNotContain(weak, chosen);

        var wide = Enumerable.Range(0, 5).Select(i => Entry(new string((char)('a' + i), 230), "SearchMemory")).ToList();
        var rendered = LessonContextContributor.Render(LessonContextContributor.Select(wide, new HashSet<string>()));
        Assert.True(rendered.Length <= LessonContextContributor.MaxCharacters);
    }

    [Fact]
    public async Task The_lessons_block_reaches_the_model_marks_the_trace_and_is_not_stored_in_the_session()
    {
        var lesson = Entry("Search before you answer from memory.", "SearchMemory");
        var trace = new TurnTraceCollector();
        var contributor = new LessonContextContributor(
            Fake<IImprovementService>.Create(("LessonsInForceAsync", _ => (IReadOnlyList<LessonEntry>)[lesson])), trace);
        var model = new CapturingModel();
        var agent = new ChatClientAgent(new ToolGroupChatClient(model), new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = [ToolGroups.CreateLoadTools(new HashSet<string>())] },
            ChatHistoryProvider = JarvisAgentFactory.CreateHistoryProvider(),
            AIContextProviders = [.. contributor.CreateProviders(new AgentBuildContext(Owner, null, Guid.NewGuid()))]
        });
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync("What did I say about coffee?", session);

        Assert.Contains(model.Texts, text => text.Contains("Search before you answer from memory."));
        Assert.Equal([lesson.ProposalId], trace.Lessons());
        Assert.DoesNotContain("Search before you answer", (await agent.SerializeSessionAsync(session)).GetRawText());
    }

    private sealed class CapturingModel : IChatClient
    {
        public List<string> Texts { get; private set; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Texts = messages.Select(message => message.Text).ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate { Role = message.Role, Contents = message.Contents };
        }
    }
}
