using Jarvis.Agents;
using Jarvis.Agents.Profiles;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Devices;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Memory;
using Jarvis.Domain.Workflows;
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
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null));

        var result = await tools.CreateReminderAsync("Call mom", "2030-01-02T09:30:00",
            cancellationToken: CancellationToken.None);

        Assert.Contains("timezone offset", result);
        Assert.Empty(reminders.Items);
    }

    [Fact]
    public async Task Create_weekday_reminder_passes_recurrence_to_the_scheduler()
    {
        var reminders = new FakeReminderService();
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(
            new DailyBriefingPreferenceRecord(OwnerId, true, new TimeOnly(8, 0), "Europe/Amsterdam", "wf", null, null)));

        var result = await tools.CreateReminderAsync("Take out the trash", "2030-01-16T07:30:00+01:00",
            "weekdays", 0, null, null, CancellationToken.None);

        var created = Assert.Single(reminders.Items);
        Assert.Equal("weekdays", created.Recurrence);
        Assert.Equal("Europe/Amsterdam", created.TimeZoneId);
        Assert.Contains("every weekday", result);
        Assert.Contains(created.Id.ToString(), result);
    }

    [Fact]
    public async Task List_reminders_shows_only_upcoming_items_soonest_first()
    {
        var reminders = new FakeReminderService();
        var later = reminders.Add("Later", DateTimeOffset.UtcNow.AddDays(2));
        var sooner = reminders.Add("Sooner", DateTimeOffset.UtcNow.AddHours(1));
        reminders.Add("Delivered", DateTimeOffset.UtcNow.AddDays(-1), "completed");
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null));

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
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null));

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
    public async Task Remember_still_succeeds_when_audit_append_fails()
    {
        var memories = new FakeMemoryService();
        var tools = CreateMemoryTools(memories, new ThrowingAuditStore());

        var result = await tools.RememberAsync("The user prefers oat milk.", "preference");

        Assert.StartsWith("Saved", result);
        Assert.Single(memories.Items);
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
    public async Task List_memories_returns_all_active_owner_memories_without_searching()
    {
        var memories = new FakeMemoryService();
        memories.Items.Add(new MemoryRecord(Guid.NewGuid(), OwnerId, "preference",
            "The user prefers short answers.", 0.7f, 0.95f, "user", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false));
        memories.Items.Add(new MemoryRecord(Guid.NewGuid(), OwnerId, "project",
            "The user is building Jarvis.", 0.7f, 0.95f, "user", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false));
        memories.Items.Add(new MemoryRecord(Guid.NewGuid(), OwnerId, "fact",
            "Expired fact.", 0.7f, 0.95f, "user", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), false));
        memories.Items.Add(new MemoryRecord(Guid.NewGuid(), Guid.NewGuid(), "fact",
            "Another user's private fact.", 0.7f, 0.95f, "user", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false));
        var tools = CreateMemoryTools(memories, new FakeAuditStore());

        var result = await tools.ListMemoriesAsync(CancellationToken.None);

        Assert.Contains("The user prefers short answers.", result);
        Assert.Contains("The user is building Jarvis.", result);
        Assert.DoesNotContain("Expired fact", result);
        Assert.DoesNotContain("Another user's private fact", result);
        Assert.Empty(memories.SearchQueries);
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
        Assert.Contains("ListMemories for a general overview", defaults);
        Assert.Contains("live Codex web search", defaults);
        Assert.Contains("RequestMcpAuthorization", defaults);
        Assert.Contains("OfferMcpSetup", defaults);
        Assert.Contains("AskForMcpCredential", defaults);
        Assert.Contains("ask the user to authorize", defaults);
        Assert.Contains("untrusted data", defaults);
        Assert.DoesNotContain(ProfileContextProvider.Prefix, defaults);
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
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null));
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

    [Fact]
    public async Task Search_memory_records_recalls_for_ranking_and_dreaming()
    {
        var memories = new FakeMemoryService();
        var commute = await memories.CreateAsync(OwnerId, "routine", "Commutes by train", 0.5f, 0.9f, null, false,
            default);
        await memories.CreateAsync(OwnerId, "fact", "Owns a cargo bike", 0.5f, 0.9f, null, false, default);

        var result = await CreateMemoryTools(memories, new FakeAuditStore()).SearchMemoryAsync("how I travel by train",
            default);

        Assert.Contains("Commutes by train", result);
        Assert.Equal([commute.Id], memories.Recalled);
    }

    [Fact]
    public void Memory_context_also_searches_with_the_previous_message_for_short_follow_ups()
    {
        Assert.Equal("We plannen de Japanreis. En wat is het budget?",
            PersonalMemoryContextProvider.FollowUpContextQuery(["We plannen de Japanreis.", "En wat is het budget?"]));
        Assert.Null(PersonalMemoryContextProvider.FollowUpContextQuery(
            ["Hoi", "Wat is het budget voor de Japanreis met het gezin in december?"]));
        Assert.Null(PersonalMemoryContextProvider.FollowUpContextQuery(["En wanneer?"]));
        Assert.Null(PersonalMemoryContextProvider.FollowUpContextQuery(["  ", "En wanneer?"]));
        Assert.Null(PersonalMemoryContextProvider.FollowUpContextQuery([]));
        Assert.Equal(new string('x', 300) + " ja",
            PersonalMemoryContextProvider.FollowUpContextQuery([new string('x', 500), "ja"]));
    }

    [Fact]
    public void Merging_context_hits_keeps_the_best_weighted_score_per_memory()
    {
        MemorySearchHit Hit(Guid id, double score) => new(new MemoryRecord(id, OwnerId, "fact", "x", 0.5f, 0.9f, "user",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false), score);
        var own = Guid.NewGuid();
        var shared = Guid.NewGuid();
        var contextOnly = Guid.NewGuid();

        var merged = MemoryRanking.MergeWithContext([Hit(own, 0.5), Hit(shared, 0.4)],
            [Hit(contextOnly, 0.5), Hit(shared, 0.45)], 1.2, maxHits: 2);

        Assert.Equal([contextOnly, shared], merged.Select(hit => hit.Memory.Id));
        Assert.Equal(0.6, merged[0].Score, 3);
        Assert.Equal(0.54, merged[1].Score, 3);
    }

    [Fact]
    public void Memory_rerank_keeps_only_the_models_known_choices_in_its_order()
    {
        MemorySearchHit Hit() => new(new MemoryRecord(Guid.NewGuid(), OwnerId, "fact", "x", 0.5f, 0.9f,
            "user", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false), 0.5);
        var candidates = Enumerable.Range(0, 5).Select(_ => Hit()).ToArray();
        var reply = $"Here: [\"{candidates[3].Memory.Id}\", \"{Guid.NewGuid()}\", \"{candidates[1].Memory.Id}\", \"{candidates[3].Memory.Id}\"]";

        var chosen = MemoryReranker.Select(candidates, reply);

        Assert.Equal([candidates[3].Memory.Id, candidates[1].Memory.Id], chosen.Select(hit => hit.Memory.Id));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData(null)]
    public void Memory_rerank_falls_back_to_the_hybrid_order_when_the_answer_is_unusable(string? reply)
    {
        var candidates = Enumerable.Range(0, 6).Select(_ => new MemorySearchHit(new MemoryRecord(Guid.NewGuid(), OwnerId,
            "fact", "x", 0.5f, 0.9f, "user", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false), 0.5)).ToArray();

        Assert.Equal(candidates.Take(3).Select(hit => hit.Memory.Id),
            MemoryReranker.Select(candidates, reply).Select(hit => hit.Memory.Id));
    }

    private static MemoryAgentTools CreateMemoryTools(FakeMemoryService memories, IAuditEventStore audit) =>
        new(memories, new MemoryReranker(new FixedChatClientResolver(new EchoContextClient()),
                NullLogger<MemoryReranker>.Instance), audit,
            new FixedUser(), NullLogger<MemoryAgentTools>.Instance);

    [Fact]
    public async Task Place_reminder_uses_the_phones_recent_position_for_here()
    {
        var reminders = new FakeReminderService();
        var telemetry = new FakeTelemetry(new DeviceTelemetryRecord(OwnerId, 52.37, 4.89, 40, null, null,
            DateTimeOffset.UtcNow.AddMinutes(-2)));
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null), telemetry);

        var result = await tools.CreatePlaceReminderAsync("Water the plants", "Home", useCurrentLocation: true,
            cancellationToken: CancellationToken.None);

        Assert.Contains("Place reminder created", result);
        var place = Assert.Single(reminders.Items).Place;
        Assert.NotNull(place);
        Assert.Equal(52.37, place.Latitude);
        Assert.Equal(Reminder.LocationArrive, place.Trigger);
        Assert.Equal(150, place.RadiusMeters);
    }

    [Fact]
    public async Task Place_reminder_without_a_recent_position_asks_for_one()
    {
        var reminders = new FakeReminderService();
        var telemetry = new FakeTelemetry(new DeviceTelemetryRecord(OwnerId, 52.37, 4.89, 40, null, null,
            DateTimeOffset.UtcNow.AddHours(-3)));
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null), telemetry);

        var result = await tools.CreatePlaceReminderAsync("Water the plants", "Home", useCurrentLocation: true,
            cancellationToken: CancellationToken.None);

        Assert.Contains("recent position", result);
        Assert.Empty(reminders.Items);
    }

    [Fact]
    public async Task Place_reminder_by_name_reuses_an_earlier_place()
    {
        var reminders = new FakeReminderService();
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null));
        await tools.CreatePlaceReminderAsync("Buy milk", "Supermarket", latitude: 52.1, longitude: 5.1,
            radiusMeters: 200, cancellationToken: CancellationToken.None);

        var result = await tools.CreatePlaceReminderAsync("Buy eggs", "supermarket", trigger: "leave",
            cancellationToken: CancellationToken.None);

        Assert.Contains("when you leave", result);
        var place = reminders.Items[^1].Place;
        Assert.NotNull(place);
        Assert.Equal((52.1, 5.1, 200d), (place.Latitude, place.Longitude, place.RadiusMeters));
        Assert.Contains("I don't know where", await tools.CreatePlaceReminderAsync("Gym", "Gym",
            cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Listing_shows_the_place_instead_of_a_time()
    {
        var reminders = new FakeReminderService();
        var tools = new ReminderAgentTools(reminders, new FixedUser(), new FakeBriefingRepository(null));
        await tools.CreatePlaceReminderAsync("Buy milk", "Supermarket", latitude: 52.1, longitude: 5.1,
            everyVisit: true, cancellationToken: CancellationToken.None);

        var list = await tools.ListRemindersAsync(cancellationToken: CancellationToken.None);

        Assert.Contains("when arriving at Supermarket (every visit): Buy milk", list);
        Assert.DoesNotContain(" due ", list);
    }

    private sealed class FakeTelemetry(DeviceTelemetryRecord? record) : IDeviceTelemetryStore
    {
        public Task<DeviceTelemetryRecord> SaveAsync(Guid ownerId, SaveDeviceTelemetryRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DeviceTelemetryRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(record);
    }

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

        public ReminderRecord Add(string title, DateTimeOffset dueAt, string status = "pending",
            string recurrence = "none", string timeZoneId = "UTC")
        {
            var record = new ReminderRecord(Guid.NewGuid(), OwnerId, title, dueAt, "wf", status, DateTimeOffset.UtcNow, null,
                recurrence, 0, timeZoneId, TimeOnly.FromDateTime(dueAt.UtcDateTime), null, null);
            Items.Add(record);
            return record;
        }

        public Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request, CancellationToken cancellationToken)
        {
            var record = Add(request.Title, request.DueAt, "pending", request.Recurrence ?? "none",
                request.TimeZoneId ?? "UTC");
            if (request.Place is null) return Task.FromResult(record);
            Items[^1] = record with { Place = request.Place };
            return Task.FromResult(Items[^1]);
        }

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

        public Task<ReminderRecord?> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset dueAt,
            CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(item => item.Id == id && item.OwnerId == ownerId &&
                item.Status is "pending" or "completed");
            if (index < 0) return Task.FromResult<ReminderRecord?>(null);
            Items[index] = Items[index] with { Status = "pending", DueAt = dueAt };
            return Task.FromResult<ReminderRecord?>(Items[index]);
        }

        public Task<ReminderRecord?> MarkDoneAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(item => item.Id == id && item.OwnerId == ownerId &&
                item.Status == "pending" && item.Recurrence == "none");
            if (index < 0) return Task.FromResult<ReminderRecord?>(null);
            Items[index] = Items[index] with { Status = "completed" };
            return Task.FromResult<ReminderRecord?>(Items[index]);
        }
    }

    private sealed class FakeMemoryService : IMemoryService
    {
        public List<MemoryRecord> Items { get; } = [];

        public Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
            DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken, string sourceType = "user",
            Guid? sourceId = null, Guid? profileId = null)
        {
            var record = new MemoryRecord(Guid.NewGuid(), ownerId, kind, content, importance, confidence, sourceType,
                sourceId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, validUntil, isPinned, profileId);
            Items.Add(record);
            return Task.FromResult(record);
        }

        public List<string> SearchQueries { get; } = [];

        public List<Guid> Recalled { get; } = [];

        public Task RecordRecallAsync(Guid ownerId, IReadOnlyCollection<Guid> memoryIds,
            CancellationToken cancellationToken)
        {
            Recalled.AddRange(memoryIds);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
            CancellationToken cancellationToken, string? kind = null, int maxHits = MemoryRanking.MaxHits)
        {
            SearchQueries.Add(query);
            return Task.FromResult<IReadOnlyList<MemorySearchHit>>(Items
                .Where(item => item.OwnerId == ownerId &&
                               item.Content.Contains(query.Split(' ')[^1].TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                .Select(item => new MemorySearchHit(item, 1)).ToArray());
        }

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
            Task.FromResult<IReadOnlyList<MemoryRecord>>(Items.Where(item => item.OwnerId == ownerId &&
                (kind is null || item.Kind == kind)).ToArray());
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

    private sealed class ThrowingAuditStore : IAuditEventStore
    {
        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass, bool success,
            Guid? approvalId, string? metadataJson, CancellationToken cancellationToken, Guid? agentRunId = null) =>
            throw new InvalidOperationException("Audit store is unavailable.");

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
