using Jarvis.Agents;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AgentToolTests
{
    private static readonly Guid OwnerId = Guid.Parse("01996b8c-6000-7000-8000-000000000001");

    [Theory]
    [InlineData("2030-01-02T09:30:00+01:00")]
    [InlineData("2030-01-02T08:30:00Z")]
    [InlineData("2030-01-02T08:30:00.000-0500")]
    public void Reminder_time_with_explicit_offset_is_accepted(string value)
    {
        Assert.True(ReminderAgentTools.TryParseDueAt(value, out var dueAt, out _));
        Assert.Equal(2030, dueAt.Year);
    }

    [Theory]
    [InlineData("2030-01-02T09:30:00")]
    [InlineData("tomorrow at nine")]
    [InlineData("")]
    public void Reminder_time_without_offset_is_rejected(string value)
    {
        Assert.False(ReminderAgentTools.TryParseDueAt(value, out _, out var problem));
        Assert.NotEmpty(problem);
    }

    [Fact]
    public async Task Create_reminder_without_offset_never_reaches_the_scheduler()
    {
        var reminders = new FakeReminderService();
        var tools = new ReminderAgentTools(reminders, new FixedUser());

        var result = await tools.CreateReminderAsync("Call mom", "2030-01-02T09:30:00", CancellationToken.None);

        Assert.Contains("timezone offset", result);
        Assert.Empty(reminders.Items);
    }

    [Fact]
    public async Task List_reminders_shows_only_upcoming_items_soonest_first()
    {
        var reminders = new FakeReminderService();
        var later = reminders.Add("Later", DateTimeOffset.UtcNow.AddDays(2));
        var sooner = reminders.Add("Sooner", DateTimeOffset.UtcNow.AddHours(1));
        reminders.Add("Delivered", DateTimeOffset.UtcNow.AddDays(-1), "completed");
        var tools = new ReminderAgentTools(reminders, new FixedUser());

        var result = await tools.ListRemindersAsync();

        Assert.DoesNotContain("Delivered", result);
        Assert.True(result.IndexOf(sooner.Id.ToString(), StringComparison.Ordinal) <
                    result.IndexOf(later.Id.ToString(), StringComparison.Ordinal));
        Assert.Contains("untrusted", result);
    }

    [Fact]
    public async Task Cancel_reminder_reports_invalid_and_missing_ids()
    {
        var reminders = new FakeReminderService();
        var existing = reminders.Add("Stretch", DateTimeOffset.UtcNow.AddHours(3));
        var tools = new ReminderAgentTools(reminders, new FixedUser());

        Assert.Contains("invalid", await tools.CancelReminderAsync("not-a-guid", CancellationToken.None));
        Assert.Contains("not found", await tools.CancelReminderAsync(Guid.NewGuid().ToString(), CancellationToken.None));
        Assert.Contains("Cancelled reminder", await tools.CancelReminderAsync(existing.Id.ToString(), CancellationToken.None));
        Assert.Equal("cancelled", reminders.Items.Single().Status);
    }

    [Theory]
    [InlineData("My password is hunter2")]
    [InlineData("api key: 12345")]
    [InlineData("Use token ghp_abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuv")]
    public void Secret_like_memories_are_detected(string content) =>
        Assert.True(MemoryAgentTools.LooksLikeSecret(content));

    [Theory]
    [InlineData("The user prefers window seats on flights")]
    [InlineData("The user forgot their password last week and found it stressful")]
    public void Ordinary_memories_are_not_flagged(string content) =>
        Assert.False(MemoryAgentTools.LooksLikeSecret(content));

    [Fact]
    public async Task Remember_saves_audits_and_deduplicates()
    {
        var memories = new FakeMemoryService();
        var audit = new FakeAuditStore();
        var tools = CreateMemoryTools(memories, audit);

        var first = await tools.RememberAsync("The user prefers oat milk.", "preference");
        var second = await tools.RememberAsync("the user prefers oat milk", "preference");

        var saved = Assert.Single(memories.Items);
        Assert.Equal("preference", saved.Kind);
        Assert.Equal("user", saved.SourceType);
        Assert.Contains(saved.Id.ToString(), first);
        Assert.Contains("already saved", second);
        Assert.Equal("memory.created", Assert.Single(audit.Actions));
    }

    [Fact]
    public async Task Remember_refuses_secrets_and_normalizes_unknown_kinds()
    {
        var memories = new FakeMemoryService();
        var tools = CreateMemoryTools(memories, new FakeAuditStore());

        Assert.Contains("credential", await tools.RememberAsync("My password is hunter2"));
        Assert.Empty(memories.Items);

        await tools.RememberAsync("The user runs on Tuesdays", "hobby", pin: true);
        var saved = Assert.Single(memories.Items);
        Assert.Equal("other", saved.Kind);
        Assert.True(saved.IsPinned);
    }

    [Fact]
    public async Task Forget_memory_deletes_owned_record_and_audits()
    {
        var memories = new FakeMemoryService();
        var audit = new FakeAuditStore();
        var tools = CreateMemoryTools(memories, audit);
        await tools.RememberAsync("The user lives in Utrecht", "fact");
        var id = memories.Items.Single().Id;

        Assert.Contains("Forgot", await tools.ForgetMemoryAsync(id.ToString(), CancellationToken.None));
        Assert.Empty(memories.Items);
        Assert.Contains("memory.deleted", audit.Actions);
        Assert.Contains("not found", await tools.ForgetMemoryAsync(id.ToString(), CancellationToken.None));
    }

    [Fact]
    public void Clock_tool_converts_to_the_requested_time_zone()
    {
        var clock = new FixedClock(new DateTimeOffset(2030, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var tools = new ClockAgentTools(clock);

        var amsterdam = tools.GetCurrentTime("Europe/Amsterdam");

        Assert.Contains("2030-07-01T14:00:00+02:00", amsterdam);
        Assert.Contains("not a known IANA time zone", tools.GetCurrentTime("Mars/Olympus"));
        Assert.Contains("2030-07-01T12:00:00+00:00", tools.GetCurrentTime());
    }

    [Fact]
    public async Task Clock_context_uses_the_owner_briefing_time_zone()
    {
        var clock = new FixedClock(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var briefings = new FakeBriefingRepository(new DailyBriefingPreferenceRecord(OwnerId, false,
            new TimeOnly(8, 0), "America/New_York", "wf", null, null));
        var provider = new ClockContextProvider(briefings, OwnerId, clock);
        var agent = new ChatClientAgent(new EchoContextClient(), new ChatClientAgentOptions
        {
            AIContextProviders = [provider]
        });

        var response = await agent.RunAsync("What time is it?");

        Assert.Contains("America/New_York", response.Text);
        Assert.Contains("2030-01-01T07:00:00-05:00", response.Text);
    }

    [Fact]
    public void Default_persona_applies_unless_configured()
    {
        var defaults = JarvisAgentFactory.BuildInstructions(null, executingTask: false);
        Assert.StartsWith("You are Jarvis, a capable, proactive personal assistant", defaults);
        Assert.Contains("Markdown", defaults);
        Assert.Contains("untrusted data", defaults);
        Assert.DoesNotContain("background task. Carry out", defaults);

        var custom = JarvisAgentFactory.BuildInstructions("You are Friday.", executingTask: true);
        Assert.StartsWith("You are Friday.", custom);
        Assert.Contains("already scheduled background task", custom);
        Assert.Contains("Never repeat or store credentials", custom);
    }

    [Fact]
    public async Task Agent_chains_list_and_cancel_reminder_tools_before_answering()
    {
        var reminders = new FakeReminderService();
        var target = reminders.Add("Dentist", DateTimeOffset.UtcNow.AddDays(1));
        var tools = new ReminderAgentTools(reminders, new FixedUser());
        var client = new ScriptedToolClient(target.Id);
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = [AIFunctionFactory.Create(tools.ListRemindersAsync), AIFunctionFactory.Create(tools.CancelReminderAsync)],
                AllowMultipleToolCalls = false
            }
        });

        var response = await agent.RunAsync("Cancel my dentist reminder");

        Assert.Equal("Done — your dentist reminder is cancelled.", response.Text);
        Assert.Equal("cancelled", reminders.Items.Single().Status);
        Assert.Equal(["ListReminders", "CancelReminder"], client.ToolNamesCalled);
    }

    private static MemoryAgentTools CreateMemoryTools(FakeMemoryService memories, FakeAuditStore audit) =>
        new(memories, new MemoryReranker(new EchoContextClient(), NullLogger<MemoryReranker>.Instance), audit, new FixedUser());

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => AgentToolTests.OwnerId;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeReminderService : IReminderService
    {
        public List<ReminderRecord> Items { get; } = [];

        public ReminderRecord Add(string title, DateTimeOffset dueAt, string status = "pending")
        {
            var record = new ReminderRecord(Guid.NewGuid(), OwnerId, title, dueAt, "wf", status, DateTimeOffset.UtcNow, null);
            Items.Add(record);
            return record;
        }

        public Task<ReminderRecord> CreateAsync(Guid ownerId, string title, DateTimeOffset dueAt, CancellationToken cancellationToken) =>
            Task.FromResult(Add(title, dueAt));

        public Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == id && item.OwnerId == ownerId));

        public Task<IReadOnlyList<ReminderRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderRecord>>(Items.Where(item => item.OwnerId == ownerId).ToArray());

        public Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(item => item.Id == id && item.OwnerId == ownerId && item.Status == "pending");
            if (index < 0) return Task.FromResult<ReminderRecord?>(null);
            Items[index] = Items[index] with { Status = "cancelled" };
            return Task.FromResult<ReminderRecord?>(Items[index]);
        }
    }

    private sealed class FakeMemoryService : IMemoryService
    {
        public List<MemoryRecord> Items { get; } = [];

        public Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
            DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken, string sourceType = "user",
            Guid? sourceId = null)
        {
            var record = new MemoryRecord(Guid.NewGuid(), ownerId, kind, content, importance, confidence, sourceType,
                sourceId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, validUntil, isPinned);
            Items.Add(record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
            CancellationToken cancellationToken, string? kind = null) =>
            Task.FromResult<IReadOnlyList<MemorySearchHit>>(Items
                .Where(item => item.OwnerId == ownerId &&
                               item.Content.Contains(query.Split(' ')[^1].TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                .Select(item => new MemorySearchHit(item, 1)).ToArray());

        public Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == id && item.OwnerId == ownerId));

        public Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            Items.RemoveAll(item => item.Id == id && item.OwnerId == ownerId);
            return Task.CompletedTask;
        }

        public Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content, float importance,
            float confidence, CancellationToken cancellationToken, string sourceType = "conversation", Guid? sourceId = null) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>(Items.ToArray());
        public Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>(Items.Where(item => item.IsPinned).ToArray());
        public Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
            float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuditStore : IAuditEventStore
    {
        public List<string> Actions { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass, bool success,
            Guid? approvalId, string? metadataJson, CancellationToken cancellationToken, Guid? agentRunId = null)
        {
            Actions.Add(action);
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass, approvalId,
                DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBriefingRepository(DailyBriefingPreferenceRecord? preference) : IDailyBriefingRepository
    {
        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(preference);
        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Answers with the text of every message it received so tests can inspect injected context.</summary>
    private sealed class EchoContextClient : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                string.Join("\n", messages.Select(message => message.Text)))));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }
    }

    /// <summary>Lists reminders, cancels the one it found, then answers — mimicking a multi-step model plan.</summary>
    private sealed class ScriptedToolClient(Guid reminderId) : IChatClient
    {
        public List<string> ToolNamesCalled { get; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            ChatMessage reply = results.Length switch
            {
                0 => Call("ListReminders", new Dictionary<string, object?>()),
                1 when results[0].Result?.ToString()?.Contains(reminderId.ToString()) == true =>
                    Call("CancelReminder", new Dictionary<string, object?> { ["reminderId"] = reminderId.ToString() }),
                _ => new ChatMessage(ChatRole.Assistant, "Done — your dentist reminder is cancelled.")
            };
            return Task.FromResult(new ChatResponse(reply));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate { Role = message.Role, Contents = message.Contents };
        }

        private ChatMessage Call(string name, Dictionary<string, object?> arguments)
        {
            ToolNamesCalled.Add(name);
            return new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments)]);
        }
    }
}
