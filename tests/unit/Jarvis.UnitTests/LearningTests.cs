using Jarvis.Agents.Learning;
using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class LearningTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000f00d");
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(22, 7, 23, true)]
    [InlineData(22, 7, 3, true)]
    [InlineData(22, 7, 12, false)]
    [InlineData(9, 17, 12, true)]
    [InlineData(9, 17, 18, false)]
    [InlineData(8, 8, 8, false)]
    public void Quiet_hours_wrap_past_midnight(int start, int end, int hour, bool quiet)
    {
        Assert.Equal(quiet, new LearningSettings(QuietHoursStart: start, QuietHoursEnd: end).IsQuietHour(hour));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(2_000)]
    public void Heartbeat_interval_is_bounded(int minutes)
    {
        Assert.Throws<ArgumentException>(() => new LearningSettings(HeartbeatMinutes: minutes).Normalize());
    }

    [Fact]
    public void Parse_tolerates_fences_prose_and_garbage()
    {
        var fenced = ReflectionService.Parse("```json\n{\"persona\":[{\"category\":\"tone\",\"statement\":\"Be direct.\",\"confidence\":0.9}]}\n```");
        Assert.Equal("Be direct.", Assert.Single(fenced.Persona!).Statement);
        Assert.Null(ReflectionService.Parse("I could not reflect.").Persona);
        Assert.Null(ReflectionService.Parse("{not json").Skills);
    }

    [Fact]
    public async Task Reflection_applies_persona_skills_and_memories_within_the_owner_settings()
    {
        var settings = new InMemorySettingsStore();
        var persona = new PersonaService(settings);
        var skills = new InMemorySkillRepository();
        var memories = new List<MemoryRecord>();
        var processed = new List<Guid>();
        var userMessage = new Message(Guid.NewGuid(), "user", "My name is Sam. Plan my Paris trip, keep it short.");
        var reply = """
            {"persona":[{"category":"format","statement":"Keep replies short.","confidence":0.8},
                        {"category":"tone","statement":"Use token ghp_abcdefghijklmnopqrstuvwxyz0123456789","confidence":0.9}],
             "skills":[{"name":"trip-planning","description":"Plan trips the way this user prefers.","instructions":"1. Confirm dates.\n2. Prefer trains."}],
             "memories":[{"kind":"fact","content":"The user's name is Sam.","importance":0.8,"confidence":0.95},
                         {"kind":"fact","content":"Weak guess about the user.","importance":0.5,"confidence":0.4}],
             "insights":["Ask about hotels"]}
            """;
        var feedbackId = Guid.NewGuid();
        var service = new ReflectionService(
            TestHistory.Create([userMessage]),
            Fake<IMessageFeedbackRepository>.Create(
                ("ListUnprocessedAsync", _ => (IReadOnlyList<MessageFeedbackRecord>)
                    [new MessageFeedbackRecord(feedbackId, Guid.NewGuid(), Guid.NewGuid(), "down", "Too long", "…", Now)]),
                ("MarkProcessedAsync", args =>
                {
                    processed.AddRange((IReadOnlyCollection<Guid>)args[1]!);
                    return System.Threading.Tasks.Task.CompletedTask;
                })),
            persona,
            skills,
            Fake<IMemoryService>.Create(
                ("ListAsync", _ => (IReadOnlyList<MemoryRecord>)memories.ToArray()),
                ("CreateAsync", args =>
                {
                    var record = new MemoryRecord(Guid.NewGuid(), Owner, (string)args[1]!, (string)args[2]!,
                        (float)args[3]!, (float)args[4]!, (string)args[8]!, (Guid?)args[9], Now, Now, null, false);
                    memories.Add(record);
                    return record;
                })),
            new RecordingNotifications(),
            new NullAudit(),
            new FixedChatClientResolver(new StaticReplyClient(reply)),
            NullLogger<ReflectionService>.Instance);

        var (outcome, latest) = await service.ReflectAsync(Owner, new LearningSettings(AutoActivateSkills: false),
            Now.AddDays(-1), default);

        Assert.Equal(1, outcome.NewPersonaTraits);
        Assert.Equal(1, outcome.SkillsSaved);
        Assert.Equal(1, outcome.MemoriesSaved);
        Assert.Equal(userMessage.CreatedAt, latest);
        Assert.Equal("Keep replies short.", Assert.Single((await persona.GetAsync(Owner, default)).TraitList).Statement);
        Assert.Equal(SkillStatuses.Proposed, Assert.Single(skills.Items).Status);
        Assert.Equal(userMessage.Id, Assert.Single(memories).SourceId);
        Assert.Equal([feedbackId], processed);
    }

    [Fact]
    public async Task Reflection_skips_the_model_when_nothing_happened()
    {
        var client = new StaticReplyClient("{}");
        var service = new ReflectionService(
            TestHistory.Create(),
            Fake<IMessageFeedbackRepository>.Create(("ListUnprocessedAsync", _ => (IReadOnlyList<MessageFeedbackRecord>)[])),
            new PersonaService(new InMemorySettingsStore()), new InMemorySkillRepository(),
            Fake<IMemoryService>.Create(), new RecordingNotifications(), new NullAudit(),
            new FixedChatClientResolver(client), NullLogger<ReflectionService>.Instance);

        var (outcome, _) = await service.ReflectAsync(Owner, LearningSettings.Default, Now.AddHours(-1), default);

        Assert.Same(ReflectionOutcome.Nothing, outcome);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Heartbeat_checks_in_once_per_item_and_stays_silent_in_quiet_hours()
    {
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.Learning, new LearningSettings(true, 60), default);
        var notifications = new RecordingNotifications();
        var clock = new MutableClock(Now);
        var heartbeat = CreateHeartbeat(settings, notifications, clock);

        var first = await heartbeat.RunAsync(Owner, default);
        var second = await heartbeat.RunAsync(Owner, default);

        Assert.Equal(3, first.CheckIns.Count);
        Assert.Contains(first.CheckIns, item => item.Text.Contains("Dentist") && item.Text.Contains("12:30"));
        Assert.Contains(first.CheckIns, item => item.Text.Contains("RunCodingTask"));
        Assert.Contains(first.CheckIns, item => item.Text.Contains("Tax research"));
        Assert.Empty(second.CheckIns);
        var notice = Assert.Single(notifications.Created);
        Assert.Equal("heartbeat.checkin", notice.Type);

        await settings.SaveAsync(Owner, LearningSections.HeartbeatState, new HeartbeatState(), default);
        clock.Now = new DateTimeOffset(2026, 9, 27, 23, 30, 0, TimeSpan.Zero);
        var quiet = await heartbeat.RunAsync(Owner, default);
        Assert.True(quiet.QuietHours);
        Assert.Empty(quiet.CheckIns);
    }

    private static HeartbeatService CreateHeartbeat(InMemorySettingsStore settings, RecordingNotifications notifications,
        MutableClock clock)
    {
        var reflection = new ReflectionService(
            TestHistory.Create(),
            Fake<IMessageFeedbackRepository>.Create(("ListUnprocessedAsync", _ => (IReadOnlyList<MessageFeedbackRecord>)[])),
            new PersonaService(settings), new InMemorySkillRepository(), Fake<IMemoryService>.Create(), notifications,
            new NullAudit(), new FixedChatClientResolver(new StaticReplyClient("{}")),
            NullLogger<ReflectionService>.Instance);
        IReadOnlyList<ReminderRecord> reminders =
        [
            new(Guid.NewGuid(), Owner, "Dentist", Now.AddMinutes(30), "wf1", "pending", Now, null),
            new(Guid.NewGuid(), Owner, "Far away", Now.AddHours(5), "wf2", "pending", Now, null),
            new(Guid.NewGuid(), Owner, "Already done", Now.AddMinutes(10), "wf3", "completed", Now, Now)
        ];
        IReadOnlyList<ToolApprovalRecord> approvals =
        [
            Approval("RunCodingTask", Now.AddMinutes(-45)),
            Approval("ForgetMemory", Now.AddMinutes(-5))
        ];
        IReadOnlyList<JarvisTaskRecord> tasks =
        [
            Task("Tax research", "failed", Now.AddHours(-1)),
            Task("Old failure", "failed", Now.AddDays(-3)),
            Task("Fine", "completed", Now.AddHours(-1))
        ];
        return new HeartbeatService(reflection, settings,
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => (DailyBriefingPreferenceRecord?)null)),
            Fake<IReminderRepository>.Create(("ListRemindersAsync", _ => reminders)),
            Fake<IToolApprovalStore>.Create(("ListActionableAsync", _ => approvals)),
            Fake<IJarvisTaskRepository>.Create(("ListAsync", _ => tasks)),
            notifications, new NullAudit(),
            Fake<Jarvis.Application.Integrations.ICalendarFeed>.Create(("ListUpcomingAsync",
                _ => (IReadOnlyList<Jarvis.Application.Integrations.CalendarEventRecord>)[])),
            Fake<IJarvisTaskService>.Create(),
            Fake<Jarvis.Application.Inbox.IInboxService>.Create(
                ("SyncAsync", _ => new Jarvis.Application.Inbox.InboxSyncResult(0, 0, 0)),
                ("ListAsync", _ => new Jarvis.Application.Inbox.InboxListing([], new Dictionary<string, int>()))),
            Fake<Jarvis.Application.Inbox.ICommitmentService>.Create(
                ("ListAsync", _ => (IReadOnlyList<Jarvis.Domain.Inbox.Commitment>)[])),
            NullLogger<HeartbeatService>.Instance, clock);
    }

    private static ToolApprovalRecord Approval(string tool, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), Owner, Guid.NewGuid(), null, "request", "call", tool, "{}", "pending", null,
            "not_started", null, createdAt, null);

    private static JarvisTaskRecord Task(string title, string status, DateTimeOffset completedAt) =>
        new(Guid.NewGuid(), Owner, title, "prompt", status, "wf", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            completedAt, completedAt, completedAt, null);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StaticReplyClient(string reply) : IChatClient
    {
        public int Calls { get; private set; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return System.Threading.Tasks.Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
