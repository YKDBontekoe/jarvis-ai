using Jarvis.Agents;
using Jarvis.Application.Conversations;
using Jarvis.Application.Learning;
using Jarvis.Application.Settings;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Profiles;
using Jarvis.Application.Profiles;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CorrectionDetectorTests
{
    [Theory]
    [InlineData("No, shorter please")]
    [InlineData("no that's not what I meant")]
    [InlineData("Nope.")]
    [InlineData("That's wrong, I live in Utrecht")]
    [InlineData("I said tomorrow, not today")]
    [InlineData("Don't use bullet points")]
    [InlineData("Stop adding disclaimers")]
    [InlineData("Actually, make it formal")]
    [InlineData("  \"Wrong.\"")]
    [InlineData("Nee, dat klopt niet")]
    [InlineData("Ik zei morgen")]
    public void Pushback_at_the_start_of_a_short_message_is_a_correction(string message) =>
        Assert.True(CorrectionDetector.LooksLikeCorrection(message));

    [Theory]
    [InlineData("Thanks, that works")]
    [InlineData("Now write the same for Friday")]
    [InlineData("Notes from the meeting are attached")]
    [InlineData("Nobody replied yet")]
    [InlineData("Nothing else")]
    [InlineData("Can you not forget to remind me?")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_messages_are_not_corrections(string? message) =>
        Assert.False(CorrectionDetector.LooksLikeCorrection(message));

    [Fact]
    public void A_long_message_that_starts_with_no_is_a_new_request()
    {
        var long_ = "No, " + new string('x', CorrectionDetector.MaxLength);
        Assert.False(CorrectionDetector.LooksLikeCorrection(long_));
    }
}

public sealed class TurnTraceTests
{
    [Fact]
    public void Collector_dedupes_and_reports_what_reached_the_model()
    {
        var collector = new TurnTraceCollector();
        var memory = Guid.NewGuid();

        collector.MemoryInjected(memory);
        collector.MemoryInjected(memory);
        collector.MemoryInjected(Guid.NewGuid());
        collector.SkillLoaded("weekly-review");
        collector.SkillLoaded("weekly-review");

        var (memories, skills) = collector.Snapshot();
        Assert.Equal(2, memories.Count);
        Assert.Equal(["weekly-review"], skills);
    }

    [Fact]
    public void A_snapshot_is_a_copy_not_a_live_view()
    {
        var collector = new TurnTraceCollector();
        var (before, _) = collector.Snapshot();

        collector.MemoryInjected(Guid.NewGuid());

        Assert.Empty(before);
    }

    [Fact]
    public void A_plain_completed_reply_that_used_nothing_is_not_worth_a_row()
    {
        TurnTraceDraft Draft(string outcome = "completed", IReadOnlyList<TurnToolCall>? tools = null,
            IReadOnlyList<Guid>? memories = null, IReadOnlyList<string>? skills = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), TurnKinds.Interactive, memories ?? [], skills ?? [], tools ?? [], 120,
                outcome);

        Assert.False(Draft().IsWorthKeeping);
        Assert.True(Draft(tools: [new TurnToolCall("SearchMemory", ToolOutcomes.Completed, 40)]).IsWorthKeeping);
        Assert.True(Draft(memories: [Guid.NewGuid()]).IsWorthKeeping);
        Assert.True(Draft(skills: ["weekly-review"]).IsWorthKeeping);
        Assert.True(Draft(outcome: "failed").IsWorthKeeping);
        Assert.True(Draft(outcome: "approval_required").IsWorthKeeping);
    }

    [Fact]
    public void Tool_failures_are_filed_by_category_never_by_exception_text()
    {
        Assert.Equal("input", ToolFailureFeedback.Kind(new ToolFailureFeedback.ToolInputException("bad", new ArgumentException())));
        Assert.Equal("transient",
            ToolFailureFeedback.Kind(new ToolFailureFeedback.ToolFailedException(new HttpRequestException("secret"), true)));
        Assert.Equal("failed",
            ToolFailureFeedback.Kind(new ToolFailureFeedback.ToolFailedException(new InvalidOperationException("secret"))));
        Assert.Equal("failed", ToolFailureFeedback.Kind(new InvalidOperationException("secret")));
    }

    [Fact]
    public void Retention_cutoffs_follow_the_owners_setting_and_keep_signals_longer()
    {
        var now = new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddDays(-30), LearningSettings.Default.TraceCutoff(now));
        Assert.Equal(now.AddDays(-14), new LearningSettings(TraceRetentionDays: 14).TraceCutoff(now));
        Assert.Equal(now.AddDays(-90), LearningSettings.SignalCutoff(now));
        Assert.Throws<ArgumentException>(() => new LearningSettings(TraceRetentionDays: 3).Normalize());
        Assert.Throws<ArgumentException>(() => new LearningSettings(TraceRetentionDays: 400).Normalize());
    }
}

public sealed class LearningRecorderTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000f1f1");
    private static readonly Guid Profile = Guid.Parse("01996b8c-6000-7000-8000-00000000f1f2");

    private sealed class Store : ILearningStore
    {
        public List<LearningSignalRecord> Signals { get; } = [];
        public List<TurnTraceRecord> Traces { get; } = [];

        public Task AddSignalAsync(LearningSignalRecord signal, CancellationToken cancellationToken)
        {
            Signals.Add(signal);
            return Task.CompletedTask;
        }

        public Task AddTraceAsync(TurnTraceRecord trace, CancellationToken cancellationToken)
        {
            Traces.Add(trace);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LearningSignalRecord>> ListSignalsAsync(Guid ownerId, DateTimeOffset since,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<TurnTraceRecord>> ListTracesAsync(Guid ownerId, DateTimeOffset since, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneAsync(Guid ownerId, DateTimeOffset tracesBefore, DateTimeOffset signalsBefore,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static (LearningRecorder Recorder, Store Store) Create(LearningSettings? settings, Conversation? conversation)
    {
        var store = new Store();
        var owner = new InMemorySettingsStore();
        if (settings is not null)
            owner.SaveAsync(Owner, SettingsSections.Learning, settings, default).GetAwaiter().GetResult();
        var services = new ServiceCollection()
            .AddSingleton<ILearningStore>(store)
            .AddSingleton<IOwnerSettingsStore>(owner)
            .AddSingleton(Fake<IConversationStore>.Create(("GetAsync", _ => conversation)))
            .BuildServiceProvider();
        return (new LearningRecorder(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LearningRecorder>.Instance), store);
    }

    private static Conversation Chat(AssistantProfileSnapshot? snapshot = null)
    {
        var conversation = new Conversation(Owner, "Chat");
        if (snapshot is not null)
            conversation.BindProfile(snapshot.ProfileId, snapshot.Version, ProfileJson.Serialize(snapshot));
        return conversation;
    }

    private static AssistantProfileSnapshot Snapshot(bool contribute) =>
        new(Profile, 1, "Work", null, null, null, null, true, false, [], false, [], null, null, null, null,
            MemoryScopes.All, true, contribute, true, true, DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_signal_is_stored_with_ids_and_names_only_and_filed_under_the_conversations_profile()
    {
        var (recorder, store) = Create(null, Chat(Snapshot(contribute: true)));
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await recorder.StoreSignalAsync(Owner, conversationId, LearningSignalKinds.ToolFailure, messageId,
            "SearchMemory", null, "transient");

        var signal = Assert.Single(store.Signals);
        Assert.Equal(Owner, signal.OwnerId);
        Assert.Equal(LearningSignalKinds.ToolFailure, signal.Kind);
        Assert.Equal(messageId, signal.MessageId);
        Assert.Equal(Profile, signal.ProfileId);
        Assert.Equal("transient", signal.ErrorKind);
        Assert.Equal("SearchMemory", signal.Tool);
    }

    [Fact]
    public async Task Nothing_is_stored_for_a_profile_that_does_not_contribute_to_learning()
    {
        var (recorder, store) = Create(null, Chat(Snapshot(contribute: false)));

        await recorder.StoreSignalAsync(Owner, Guid.NewGuid(), LearningSignalKinds.Regenerate, null, null, null, null);
        await recorder.StoreTraceAsync(Owner, new TurnTraceDraft(Guid.NewGuid(), null, TurnKinds.Interactive,
            [Guid.NewGuid()], [], [], 10, "completed"));

        Assert.Empty(store.Signals);
        Assert.Empty(store.Traces);
    }

    [Fact]
    public async Task Switching_capture_off_stops_both_signals_and_traces()
    {
        var (recorder, store) = Create(new LearningSettings(CaptureSignals: false), Chat());

        await recorder.StoreSignalAsync(Owner, Guid.NewGuid(), LearningSignalKinds.ThumbsDown, null, null, null, null);
        await recorder.StoreTraceAsync(Owner, new TurnTraceDraft(Guid.NewGuid(), null, TurnKinds.Task, [],
            ["weekly-review"], [], 10, "completed"));

        Assert.Empty(store.Signals);
        Assert.Empty(store.Traces);
    }

    [Fact]
    public async Task A_missing_conversation_or_an_unknown_kind_stores_nothing()
    {
        var (missing, missingStore) = Create(null, null);
        await missing.StoreSignalAsync(Owner, Guid.NewGuid(), LearningSignalKinds.Regenerate, null, null, null, null);
        Assert.Empty(missingStore.Signals);

        var (recorder, store) = Create(null, Chat());
        await recorder.StoreSignalAsync(Owner, Guid.NewGuid(), "made_up", null, null, null, null);
        Assert.Empty(store.Signals);
    }

    [Fact]
    public async Task A_trace_keeps_what_the_run_collected_and_the_profile()
    {
        var (recorder, store) = Create(null, Chat(Snapshot(contribute: true)));
        var memory = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await recorder.StoreTraceAsync(Owner, new TurnTraceDraft(Guid.NewGuid(), messageId, TurnKinds.Interactive,
            [memory], ["weekly-review"], [new TurnToolCall("SearchMemory", ToolOutcomes.Completed, 35)], 900,
            "completed"));

        var trace = Assert.Single(store.Traces);
        Assert.Equal(messageId, trace.MessageId);
        Assert.Equal(Profile, trace.ProfileId);
        Assert.Equal([memory], trace.MemoryIds);
        Assert.Equal(["weekly-review"], trace.Skills);
        Assert.Equal(35, Assert.Single(trace.Tools).Ms);
    }

    [Fact]
    public void Recording_returns_at_once_and_never_throws_even_with_no_store_behind_it()
    {
        var empty = new ServiceCollection().BuildServiceProvider();
        var recorder = new LearningRecorder(empty.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LearningRecorder>.Instance);

        recorder.RecordSignal(Owner, Guid.NewGuid(), LearningSignalKinds.Regenerate);
        recorder.RecordTrace(Owner, new TurnTraceDraft(Guid.NewGuid(), null, TurnKinds.Interactive, [], [], [], 1,
            "failed"));
    }
}
